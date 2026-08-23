using CloudSource.Playnite.Providers.GoogleDrive;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;

namespace CloudSource.Playnite
{
    internal sealed class GoogleDriveFolderPickerViewModel : ObservableObject, IDisposable
    {
        private sealed class BrowseFrame
        {
            public GoogleDriveFolder Location { get; }
            public IReadOnlyList<GoogleDriveFolder> Children { get; }

            public BrowseFrame(
                GoogleDriveFolder location,
                IReadOnlyList<GoogleDriveFolder> children)
            {
                Location = location ?? throw new ArgumentNullException(nameof(location));
                Children = children ?? throw new ArgumentNullException(nameof(children));
            }
        }

        private readonly IGoogleDriveFolderBrowser browser;
        private readonly GoogleDriveAccountConfiguration configuration;
        private readonly GoogleDriveAuthorization draftAuthorization;
        private readonly Action<GoogleDriveFolder> selectFolder;
        private readonly Action cancel;
        private readonly Action<string> showError;
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly List<BrowseFrame> history = new List<BrowseFrame>();
        private bool isBusy;
        private string currentPath;
        private string status;

        public ObservableCollection<GoogleDriveFolder> Folders { get; } =
            new ObservableCollection<GoogleDriveFolder>();

        public string ExistingSelectionPath { get; }

        public bool IsBusy
        {
            get => isBusy;
            private set
            {
                SetValue(ref isBusy, value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string CurrentPath
        {
            get => currentPath;
            private set => SetValue(ref currentPath, value);
        }

        public string Status
        {
            get => status;
            private set => SetValue(ref status, value);
        }

        public RelayCommand<GoogleDriveFolder> OpenFolderCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand UseCurrentFolderCommand { get; }
        public RelayCommand CancelCommand { get; }

        public GoogleDriveFolderPickerViewModel(
            IGoogleDriveFolderBrowser browser,
            GoogleDriveAccountConfiguration configuration,
            GoogleDriveAuthorization draftAuthorization,
            string existingSelectionPath,
            Action<GoogleDriveFolder> selectFolder,
            Action cancel,
            Action<string> showError)
        {
            this.browser = browser ?? throw new ArgumentNullException(nameof(browser));
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.draftAuthorization = draftAuthorization;
            this.selectFolder = selectFolder ?? throw new ArgumentNullException(nameof(selectFolder));
            this.cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
            this.showError = showError ?? throw new ArgumentNullException(nameof(showError));
            ExistingSelectionPath = string.IsNullOrWhiteSpace(existingSelectionPath)
                ? "No source folder selected"
                : existingSelectionPath.Trim();

            OpenFolderCommand = new RelayCommand<GoogleDriveFolder>(
                OpenFolder,
                folder => !IsBusy && folder?.CanBrowse == true);
            BackCommand = new RelayCommand(
                GoBack,
                () => !IsBusy && history.Count > 1);
            UseCurrentFolderCommand = new RelayCommand(
                UseCurrentFolder,
                () => !IsBusy && history.LastOrDefault()?.Location.CanSelect == true);
            CancelCommand = new RelayCommand(cancel);

            var providerRoot = browser.ProviderRoot;
            history.Add(new BrowseFrame(providerRoot, browser.GetDriveLocations()));
            ShowCurrentFrame("Choose My Drive or Shared with me.");
        }

        public void Dispose()
        {
            cancellationSource.Cancel();
            cancellationSource.Dispose();
        }

        private async void OpenFolder(GoogleDriveFolder folder)
        {
            if (folder == null || IsBusy)
            {
                return;
            }

            IsBusy = true;
            Status = $"Loading {folder.DisplayPath}...";
            try
            {
                var children = await browser.BrowseAsync(
                    configuration,
                    folder,
                    draftAuthorization,
                    cancellationSource.Token);
                cancellationSource.Token.ThrowIfCancellationRequested();
                history.Add(new BrowseFrame(folder, children));
                var message = children.Count == 0
                    ? "This folder contains no child folders. You can still use it as the source."
                    : $"{children.Count} child folder(s). Open one or use the current folder.";
                ShowCurrentFrame(message);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Status = "Could not load Google Drive folders.";
                showError(exception.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void GoBack()
        {
            if (IsBusy || history.Count <= 1)
            {
                return;
            }

            history.RemoveAt(history.Count - 1);
            ShowCurrentFrame("Choose a folder location.");
        }

        private void UseCurrentFolder()
        {
            var folder = history.LastOrDefault()?.Location;
            if (IsBusy || folder?.CanSelect != true)
            {
                return;
            }

            selectFolder(folder);
        }

        private void ShowCurrentFrame(string message)
        {
            var frame = history.Last();
            Folders.Clear();
            foreach (var folder in frame.Children)
            {
                Folders.Add(folder);
            }

            CurrentPath = frame.Location.Kind == GoogleDriveFolderKind.ProviderRoot
                ? "Google Drive"
                : "Google Drive / " + frame.Location.DisplayPath.Replace("/", " / ");
            Status = message;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
