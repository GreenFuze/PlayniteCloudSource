using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Providers.GoogleDrive;
using CloudSource.Playnite.Providers.OneDrive;
using CloudSource.Playnite.Storage;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;

namespace CloudSource.Playnite
{
    public sealed class CloudSourceSettings : ObservableObject
    {
        private string managedRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Games",
            CloudStorageProduct.DisplayName);
        private bool googleDriveEnabled;
        private string googleDriveAccountId;
        private string googleDriveAccountDisplayName;
        private string googleDriveFolderId = "root";
        private string googleDriveFolderDisplayPath = "My Drive";
        private bool oneDriveEnabled;
        private string oneDriveAccountId;
        private string oneDriveAccountDisplayName;
        private string oneDriveFolderId = "root";
        private string oneDriveFolderDisplayPath = "OneDrive";

        public string ManagedRootPath
        {
            get => managedRootPath;
            set => SetValue(ref managedRootPath, value);
        }

        public bool GoogleDriveEnabled
        {
            get => googleDriveEnabled;
            set => SetValue(ref googleDriveEnabled, value);
        }

        public string GoogleDriveAccountId
        {
            get => googleDriveAccountId;
            set => SetValue(ref googleDriveAccountId, value);
        }

        public string GoogleDriveAccountDisplayName
        {
            get => googleDriveAccountDisplayName;
            set => SetValue(ref googleDriveAccountDisplayName, value);
        }

        public string GoogleDriveFolderId
        {
            get => googleDriveFolderId;
            set => SetValue(ref googleDriveFolderId, value);
        }

        public string GoogleDriveFolderDisplayPath
        {
            get => googleDriveFolderDisplayPath;
            set => SetValue(ref googleDriveFolderDisplayPath, value);
        }

        public bool OneDriveEnabled
        {
            get => oneDriveEnabled;
            set => SetValue(ref oneDriveEnabled, value);
        }

        public string OneDriveAccountId
        {
            get => oneDriveAccountId;
            set => SetValue(ref oneDriveAccountId, value);
        }

        public string OneDriveAccountDisplayName
        {
            get => oneDriveAccountDisplayName;
            set => SetValue(ref oneDriveAccountDisplayName, value);
        }

        public string OneDriveFolderId
        {
            get => oneDriveFolderId;
            set => SetValue(ref oneDriveFolderId, value);
        }

        public string OneDriveFolderDisplayPath
        {
            get => oneDriveFolderDisplayPath;
            set => SetValue(ref oneDriveFolderDisplayPath, value);
        }

        public bool HasConcreteGoogleDriveFolder =>
            !string.IsNullOrWhiteSpace(GoogleDriveFolderId) &&
            !string.Equals(GoogleDriveFolderId.Trim(), "root", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(GoogleDriveFolderDisplayPath) &&
            !string.Equals(GoogleDriveFolderDisplayPath.Trim(), "My Drive", StringComparison.OrdinalIgnoreCase);

        public bool HasConcreteOneDriveFolder =>
            !string.IsNullOrWhiteSpace(OneDriveFolderId) &&
            !string.Equals(OneDriveFolderId.Trim(), "root", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(OneDriveFolderDisplayPath) &&
            !string.Equals(OneDriveFolderDisplayPath.Trim(), "OneDrive", StringComparison.OrdinalIgnoreCase);

        internal GoogleDriveProviderConfiguration CreateGoogleDriveProviderConfiguration()
        {
            return new GoogleDriveProviderConfiguration(
                GoogleDriveEnabled,
                GoogleDriveAccountId,
                GoogleDriveAccountDisplayName,
                GoogleDriveFolderId,
                GoogleDriveFolderDisplayPath);
        }

        internal OneDriveProviderConfiguration CreateOneDriveProviderConfiguration()
        {
            return new OneDriveProviderConfiguration(
                OneDriveEnabled,
                OneDriveAccountId,
                OneDriveAccountDisplayName,
                OneDriveFolderId,
                OneDriveFolderDisplayPath);
        }
    }

    public sealed class CloudSourceSettingsViewModel : ObservableObject, ISettings
    {
        private readonly CloudSourcePlugin plugin;
        private readonly ICloudSourceProvider googleDriveProvider;
        private readonly ICloudSourceProvider oneDriveProvider;
        private CloudSourceSettings editingClone;
        private CloudSourceSettings settings;
        private bool pendingGoogleDriveDisconnect;
        private bool googleDriveBusy;
        private string googleDriveStatus;
        private bool pendingOneDriveDisconnect;
        private bool oneDriveBusy;
        private string oneDriveStatus;

        public CloudSourceSettings Settings
        {
            get => settings;
            private set => SetValue(ref settings, value);
        }

        public bool GoogleDriveBusy
        {
            get => googleDriveBusy;
            private set => SetValue(ref googleDriveBusy, value);
        }

        public string GoogleDriveStatus
        {
            get => googleDriveStatus;
            private set => SetValue(ref googleDriveStatus, value);
        }

        public bool OneDriveBusy
        {
            get => oneDriveBusy;
            private set => SetValue(ref oneDriveBusy, value);
        }

        public string OneDriveStatus
        {
            get => oneDriveStatus;
            private set => SetValue(ref oneDriveStatus, value);
        }

        public string GoogleDriveFolderSelectionStatus => Settings.HasConcreteGoogleDriveFolder
            ? Settings.GoogleDriveFolderDisplayPath
            : "No concrete folder selected";

        public string OneDriveFolderSelectionStatus => Settings.HasConcreteOneDriveFolder
            ? Settings.OneDriveFolderDisplayPath
            : "No concrete folder selected";

        public RelayCommand ConnectGoogleDriveCommand { get; }
        public RelayCommand DisconnectGoogleDriveCommand { get; }
        public RelayCommand ChooseGoogleDriveFolderCommand { get; }
        public RelayCommand ConnectOneDriveCommand { get; }
        public RelayCommand DisconnectOneDriveCommand { get; }
        public RelayCommand ChooseOneDriveFolderCommand { get; }

        public CloudSourceSettingsViewModel(
            CloudSourcePlugin plugin,
            ICloudSourceProvider googleDriveProvider,
            ICloudSourceProvider oneDriveProvider)
        {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            this.googleDriveProvider = googleDriveProvider ?? throw new ArgumentNullException(nameof(googleDriveProvider));
            if (!string.Equals(googleDriveProvider.Id, GoogleDriveProvider.ProviderId, StringComparison.Ordinal))
                throw new ArgumentException("The settings provider must be Google Drive.", nameof(googleDriveProvider));
            this.oneDriveProvider = oneDriveProvider ?? throw new ArgumentNullException(nameof(oneDriveProvider));
            if (!string.Equals(oneDriveProvider.Id, OneDriveProvider.ProviderId, StringComparison.Ordinal))
                throw new ArgumentException("The second settings provider must be OneDrive.", nameof(oneDriveProvider));
            Settings = plugin.LoadPluginSettings<CloudSourceSettings>() ?? new CloudSourceSettings();
            ConnectGoogleDriveCommand = new RelayCommand(ConnectGoogleDrive);
            DisconnectGoogleDriveCommand = new RelayCommand(DisconnectGoogleDrive);
            ChooseGoogleDriveFolderCommand = new RelayCommand(
                ChooseGoogleDriveFolder,
                () => !GoogleDriveBusy);
            ConnectOneDriveCommand = new RelayCommand(ConnectOneDrive);
            DisconnectOneDriveCommand = new RelayCommand(DisconnectOneDrive);
            ChooseOneDriveFolderCommand = new RelayCommand(
                ChooseOneDriveFolder,
                () => !OneDriveBusy);
            RefreshGoogleDriveStatus();
            RefreshOneDriveStatus();
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
            googleDriveProvider.DiscardPendingConnection();
            oneDriveProvider.DiscardPendingConnection();
            pendingGoogleDriveDisconnect = false;
            pendingOneDriveDisconnect = false;
            RefreshGoogleDriveStatus();
            RefreshOneDriveStatus();
            OnPropertyChanged(nameof(GoogleDriveFolderSelectionStatus));
            OnPropertyChanged(nameof(OneDriveFolderSelectionStatus));
        }

        public void CancelEdit()
        {
            Settings = editingClone ?? new CloudSourceSettings();
            googleDriveProvider.DiscardPendingConnection();
            oneDriveProvider.DiscardPendingConnection();
            pendingGoogleDriveDisconnect = false;
            pendingOneDriveDisconnect = false;
            RefreshGoogleDriveStatus();
            RefreshOneDriveStatus();
            OnPropertyChanged(nameof(GoogleDriveFolderSelectionStatus));
            OnPropertyChanged(nameof(OneDriveFolderSelectionStatus));
        }

        public void EndEdit()
        {
            if (!ManagedStorageLayout.TryCreate(Settings.ManagedRootPath, out var layout, out var error))
            {
                throw new InvalidOperationException(error);
            }

            layout.EnsureCreated();
            Settings.ManagedRootPath = layout.RootPath;

            if (googleDriveProvider.HasPendingConnection) googleDriveProvider.CommitPendingConnection();
            if (oneDriveProvider.HasPendingConnection) oneDriveProvider.CommitPendingConnection();
            plugin.SavePluginSettings(Settings);
            if (pendingGoogleDriveDisconnect) googleDriveProvider.Disconnect();
            if (pendingOneDriveDisconnect) oneDriveProvider.Disconnect();

            pendingGoogleDriveDisconnect = false;
            pendingOneDriveDisconnect = false;
            editingClone = null;
            RefreshGoogleDriveStatus();
            RefreshOneDriveStatus();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (!ManagedStorageLayout.TryCreate(Settings.ManagedRootPath, out _, out var rootError))
            {
                errors.Add(rootError);
            }

            if (Settings.GoogleDriveEnabled)
            {
                if (string.IsNullOrWhiteSpace(Settings.GoogleDriveAccountId) ||
                    string.IsNullOrWhiteSpace(Settings.GoogleDriveAccountDisplayName))
                {
                    errors.Add("Connect a Google Drive account before enabling the source.");
                }

                if (!Settings.HasConcreteGoogleDriveFolder)
                {
                    errors.Add("Choose a concrete Google Drive source folder. My Drive root is intentionally not scanned.");
                }

                if (!googleDriveProvider.HasPendingConnection && !googleDriveProvider.HasStoredConnection)
                {
                    errors.Add("Google Drive authorization is missing. Connect the account again.");
                }
            }

            if (Settings.OneDriveEnabled)
            {
                if (string.IsNullOrWhiteSpace(Settings.OneDriveAccountId) ||
                    string.IsNullOrWhiteSpace(Settings.OneDriveAccountDisplayName))
                {
                    errors.Add("Connect a Microsoft account before enabling the OneDrive source.");
                }
                if (!Settings.HasConcreteOneDriveFolder)
                {
                    errors.Add("Choose a concrete OneDrive source folder. The OneDrive root is intentionally not scanned.");
                }
                if (!oneDriveProvider.HasPendingConnection && !oneDriveProvider.HasStoredConnection)
                {
                    errors.Add("OneDrive authorization is missing. Connect the account again.");
                }
            }

            return errors.Count == 0;
        }

        private async void ConnectGoogleDrive()
        {
            if (GoogleDriveBusy)
            {
                return;
            }

            GoogleDriveBusy = true;
            GoogleDriveStatus = "Waiting for Google account authorization...";
            try
            {
                var authorization = await googleDriveProvider.ConnectAsync(CancellationToken.None);
                var accountChanged = !string.Equals(
                    Settings.GoogleDriveAccountId,
                    authorization.Id,
                    StringComparison.Ordinal);
                pendingGoogleDriveDisconnect = false;
                Settings.GoogleDriveEnabled = true;
                Settings.GoogleDriveAccountId = authorization.Id;
                Settings.GoogleDriveAccountDisplayName = authorization.DisplayName;
                if (accountChanged)
                {
                    ClearGoogleDriveFolder();
                }

                GoogleDriveStatus = $"Connected draft: {authorization.DisplayName}. Save settings to commit.";
            }
            catch (Exception exception)
            {
                GoogleDriveStatus = "Google Drive connection failed.";
                plugin.ShowError(exception.Message);
            }
            finally
            {
                GoogleDriveBusy = false;
            }
        }

        private void DisconnectGoogleDrive()
        {
            if (GoogleDriveBusy)
            {
                return;
            }

            googleDriveProvider.DiscardPendingConnection();
            pendingGoogleDriveDisconnect = true;
            Settings.GoogleDriveEnabled = false;
            Settings.GoogleDriveAccountId = null;
            Settings.GoogleDriveAccountDisplayName = null;
            ClearGoogleDriveFolder();
            GoogleDriveStatus = "Disconnected draft. Save settings to remove the stored authorization.";
        }

        private void ChooseGoogleDriveFolder()
        {
            if (GoogleDriveBusy)
            {
                return;
            }

            if (!googleDriveProvider.HasPendingConnection && !googleDriveProvider.HasStoredConnection)
            {
                plugin.ShowError("Connect a Google Drive account before choosing a source folder.");
                return;
            }

            try
            {
                var selection = googleDriveProvider.SelectSourceFolder(GoogleDriveFolderSelectionStatus);
                if (selection == null)
                {
                    return;
                }

                Settings.GoogleDriveFolderId = selection.ObjectId;
                Settings.GoogleDriveFolderDisplayPath = selection.DisplayPath;
                OnPropertyChanged(nameof(GoogleDriveFolderSelectionStatus));
            }
            catch (Exception exception)
            {
                plugin.ShowError(exception.Message);
            }
        }

        private async void ConnectOneDrive()
        {
            if (OneDriveBusy) return;

            OneDriveBusy = true;
            OneDriveStatus = "Waiting for Microsoft account authorization...";
            try
            {
                var authorization = await oneDriveProvider.ConnectAsync(CancellationToken.None);
                var accountChanged = !string.Equals(
                    Settings.OneDriveAccountId,
                    authorization.Id,
                    StringComparison.Ordinal);
                pendingOneDriveDisconnect = false;
                Settings.OneDriveEnabled = true;
                Settings.OneDriveAccountId = authorization.Id;
                Settings.OneDriveAccountDisplayName = authorization.DisplayName;
                if (accountChanged) ClearOneDriveFolder();
                OneDriveStatus = $"Connected draft: {authorization.DisplayName}. Save settings to commit.";
            }
            catch (Exception exception)
            {
                OneDriveStatus = "OneDrive connection failed.";
                plugin.ShowError(exception.Message);
            }
            finally
            {
                OneDriveBusy = false;
            }
        }

        private void DisconnectOneDrive()
        {
            if (OneDriveBusy) return;
            oneDriveProvider.DiscardPendingConnection();
            pendingOneDriveDisconnect = true;
            Settings.OneDriveEnabled = false;
            Settings.OneDriveAccountId = null;
            Settings.OneDriveAccountDisplayName = null;
            ClearOneDriveFolder();
            OneDriveStatus = "Disconnected draft. Save settings to remove the stored authorization.";
        }

        private void ChooseOneDriveFolder()
        {
            if (OneDriveBusy) return;
            if (!oneDriveProvider.HasPendingConnection && !oneDriveProvider.HasStoredConnection)
            {
                plugin.ShowError("Connect a Microsoft account before choosing a OneDrive source folder.");
                return;
            }
            try
            {
                var selection = oneDriveProvider.SelectSourceFolder(OneDriveFolderSelectionStatus);
                if (selection == null) return;
                Settings.OneDriveFolderId = selection.ObjectId;
                Settings.OneDriveFolderDisplayPath = selection.DisplayPath;
                OnPropertyChanged(nameof(OneDriveFolderSelectionStatus));
            }
            catch (Exception exception)
            {
                plugin.ShowError(exception.Message);
            }
        }

        private void ClearGoogleDriveFolder()
        {
            Settings.GoogleDriveFolderId = null;
            Settings.GoogleDriveFolderDisplayPath = null;
            OnPropertyChanged(nameof(GoogleDriveFolderSelectionStatus));
        }

        private void ClearOneDriveFolder()
        {
            Settings.OneDriveFolderId = null;
            Settings.OneDriveFolderDisplayPath = null;
            OnPropertyChanged(nameof(OneDriveFolderSelectionStatus));
        }

        private void RefreshGoogleDriveStatus()
        {
            if (Settings.GoogleDriveEnabled &&
                googleDriveProvider.HasStoredConnection &&
                !string.IsNullOrWhiteSpace(Settings.GoogleDriveAccountDisplayName))
            {
                GoogleDriveStatus = $"Connected: {Settings.GoogleDriveAccountDisplayName}";
            }
            else
            {
                GoogleDriveStatus = "Not connected";
            }
        }

        private void RefreshOneDriveStatus()
        {
            if (Settings.OneDriveEnabled &&
                oneDriveProvider.HasStoredConnection &&
                !string.IsNullOrWhiteSpace(Settings.OneDriveAccountDisplayName))
            {
                OneDriveStatus = $"Connected: {Settings.OneDriveAccountDisplayName}";
            }
            else
            {
                OneDriveStatus = "Not connected";
            }
        }
    }
}
