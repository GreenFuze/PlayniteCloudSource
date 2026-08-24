using CloudSource.Playnite.Providers.OneDrive;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;

namespace CloudSource.Playnite
{
    internal sealed class OneDriveFolderPickerViewModel : ObservableObject, IDisposable
    {
        private sealed class BrowseFrame
        {
            public OneDriveFolder Location { get; }
            public IReadOnlyList<OneDriveFolder> Children { get; }

            public BrowseFrame(OneDriveFolder location, IReadOnlyList<OneDriveFolder> children)
            {
                Location = location ?? throw new ArgumentNullException(nameof(location));
                Children = children ?? throw new ArgumentNullException(nameof(children));
            }
        }

        private readonly IOneDriveFolderBrowser browser;
        private readonly OneDriveAccountConfiguration configuration;
        private readonly OneDriveAuthorization draftAuthorization;
        private readonly Action<OneDriveFolder> selectFolder;
        private readonly Action<string> showError;
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly List<BrowseFrame> history = new List<BrowseFrame>();
        private bool isBusy;
        private string currentPath;
        private string status;

        public ObservableCollection<OneDriveFolder> Folders { get; } = new ObservableCollection<OneDriveFolder>();
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

        public RelayCommand<OneDriveFolder> OpenFolderCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand UseCurrentFolderCommand { get; }
        public RelayCommand CancelCommand { get; }

        public OneDriveFolderPickerViewModel(
            IOneDriveFolderBrowser browser,
            OneDriveAccountConfiguration configuration,
            OneDriveAuthorization draftAuthorization,
            string existingSelectionPath,
            Action<OneDriveFolder> selectFolder,
            Action cancel,
            Action<string> showError)
        {
            this.browser = browser ?? throw new ArgumentNullException(nameof(browser));
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.draftAuthorization = draftAuthorization;
            this.selectFolder = selectFolder ?? throw new ArgumentNullException(nameof(selectFolder));
            if (cancel == null) throw new ArgumentNullException(nameof(cancel));
            this.showError = showError ?? throw new ArgumentNullException(nameof(showError));
            ExistingSelectionPath = string.IsNullOrWhiteSpace(existingSelectionPath)
                ? "No source folder selected"
                : existingSelectionPath.Trim();
            OpenFolderCommand = new RelayCommand<OneDriveFolder>(
                OpenFolder,
                folder => !IsBusy && folder?.CanBrowse == true);
            BackCommand = new RelayCommand(GoBack, () => !IsBusy && history.Count > 1);
            UseCurrentFolderCommand = new RelayCommand(
                UseCurrentFolder,
                () => !IsBusy && history.LastOrDefault()?.Location.CanSelect == true);
            CancelCommand = new RelayCommand(cancel);
            var providerRoot = browser.ProviderRoot;
            history.Add(new BrowseFrame(providerRoot, browser.GetDriveLocations()));
            ShowCurrentFrame("Open My files, then choose a concrete folder.");
        }

        public void Dispose()
        {
            cancellationSource.Cancel();
            cancellationSource.Dispose();
        }

        private async void OpenFolder(OneDriveFolder folder)
        {
            if (folder == null || IsBusy) return;
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
                ShowCurrentFrame(children.Count == 0
                    ? "This folder contains no child folders. You can still use it as the source."
                    : $"{children.Count} child folder(s). Open one or use the current folder.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Status = "Could not load OneDrive folders.";
                showError(exception.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void GoBack()
        {
            if (IsBusy || history.Count <= 1) return;
            history.RemoveAt(history.Count - 1);
            ShowCurrentFrame("Choose a folder location.");
        }

        private void UseCurrentFolder()
        {
            var folder = history.LastOrDefault()?.Location;
            if (IsBusy || folder?.CanSelect != true) return;
            selectFolder(folder);
        }

        private void ShowCurrentFrame(string message)
        {
            var frame = history.Last();
            Folders.Clear();
            foreach (var folder in frame.Children) Folders.Add(folder);
            CurrentPath = frame.Location.Kind == OneDriveFolderKind.ProviderRoot
                ? "OneDrive"
                : frame.Location.DisplayPath.Replace("/", " / ");
            Status = message;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
