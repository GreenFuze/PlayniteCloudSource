using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Providers.GoogleDrive;
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
        private string googleDriveClientId;
        private string googleDriveClientSecret;
        private string googleDriveAccountId;
        private string googleDriveAccountDisplayName;
        private string googleDriveFolderId = "root";
        private string googleDriveFolderDisplayPath = "My Drive";

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

        public string GoogleDriveClientId
        {
            get => googleDriveClientId;
            set => SetValue(ref googleDriveClientId, value);
        }

        public string GoogleDriveClientSecret
        {
            get => googleDriveClientSecret;
            set => SetValue(ref googleDriveClientSecret, value);
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

        public bool HasConcreteGoogleDriveFolder =>
            !string.IsNullOrWhiteSpace(GoogleDriveFolderId) &&
            !string.Equals(GoogleDriveFolderId.Trim(), "root", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(GoogleDriveFolderDisplayPath) &&
            !string.Equals(GoogleDriveFolderDisplayPath.Trim(), "My Drive", StringComparison.OrdinalIgnoreCase);

        internal GoogleDriveProviderConfiguration CreateGoogleDriveProviderConfiguration()
        {
            return new GoogleDriveProviderConfiguration(
                GoogleDriveEnabled,
                GoogleDriveClientId,
                GoogleDriveClientSecret,
                GoogleDriveAccountId,
                GoogleDriveAccountDisplayName,
                GoogleDriveFolderId,
                GoogleDriveFolderDisplayPath);
        }
    }

    public sealed class CloudSourceSettingsViewModel : ObservableObject, ISettings
    {
        private readonly CloudSourcePlugin plugin;
        private readonly ICloudSourceProvider googleDriveProvider;
        private CloudSourceSettings editingClone;
        private CloudSourceSettings settings;
        private bool pendingGoogleDriveDisconnect;
        private bool googleDriveBusy;
        private string googleDriveStatus;

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

        public string GoogleDriveFolderSelectionStatus => Settings.HasConcreteGoogleDriveFolder
            ? Settings.GoogleDriveFolderDisplayPath
            : "No concrete folder selected";

        public RelayCommand ConnectGoogleDriveCommand { get; }
        public RelayCommand DisconnectGoogleDriveCommand { get; }
        public RelayCommand ChooseGoogleDriveFolderCommand { get; }

        public CloudSourceSettingsViewModel(
            CloudSourcePlugin plugin,
            ICloudSourceProvider googleDriveProvider)
        {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            this.googleDriveProvider = googleDriveProvider ?? throw new ArgumentNullException(nameof(googleDriveProvider));
            if (!string.Equals(googleDriveProvider.Id, GoogleDriveProvider.ProviderId, StringComparison.Ordinal))
                throw new ArgumentException("The settings provider must be Google Drive.", nameof(googleDriveProvider));
            Settings = plugin.LoadPluginSettings<CloudSourceSettings>() ?? new CloudSourceSettings();
            ConnectGoogleDriveCommand = new RelayCommand(ConnectGoogleDrive);
            DisconnectGoogleDriveCommand = new RelayCommand(DisconnectGoogleDrive);
            ChooseGoogleDriveFolderCommand = new RelayCommand(
                ChooseGoogleDriveFolder,
                () => !GoogleDriveBusy);
            RefreshGoogleDriveStatus();
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
            googleDriveProvider.DiscardPendingConnection();
            pendingGoogleDriveDisconnect = false;
            RefreshGoogleDriveStatus();
            OnPropertyChanged(nameof(GoogleDriveFolderSelectionStatus));
        }

        public void CancelEdit()
        {
            Settings = editingClone ?? new CloudSourceSettings();
            googleDriveProvider.DiscardPendingConnection();
            pendingGoogleDriveDisconnect = false;
            RefreshGoogleDriveStatus();
            OnPropertyChanged(nameof(GoogleDriveFolderSelectionStatus));
        }

        public void EndEdit()
        {
            if (!ManagedStorageLayout.TryCreate(Settings.ManagedRootPath, out var layout, out var error))
            {
                throw new InvalidOperationException(error);
            }

            layout.EnsureCreated();
            Settings.ManagedRootPath = layout.RootPath;

            if (googleDriveProvider.HasPendingConnection)
            {
                googleDriveProvider.CommitPendingConnection();
                plugin.SavePluginSettings(Settings);
            }
            else if (pendingGoogleDriveDisconnect)
            {
                plugin.SavePluginSettings(Settings);
                googleDriveProvider.Disconnect();
            }
            else
            {
                plugin.SavePluginSettings(Settings);
            }

            pendingGoogleDriveDisconnect = false;
            editingClone = null;
            RefreshGoogleDriveStatus();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (!ManagedStorageLayout.TryCreate(Settings.ManagedRootPath, out _, out var rootError))
            {
                errors.Add(rootError);
            }

            var hasClientId = !string.IsNullOrWhiteSpace(Settings.GoogleDriveClientId);
            var hasClientSecret = !string.IsNullOrWhiteSpace(Settings.GoogleDriveClientSecret);
            if (hasClientId != hasClientSecret)
            {
                errors.Add("Google Drive client ID and client secret must be provided together.");
            }

            if (Settings.GoogleDriveEnabled)
            {
                if (!hasClientId)
                {
                    errors.Add("Google Drive client credentials are required before connecting.");
                }

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

            return errors.Count == 0;
        }

        private async void ConnectGoogleDrive()
        {
            if (GoogleDriveBusy)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(Settings.GoogleDriveClientId) ||
                string.IsNullOrWhiteSpace(Settings.GoogleDriveClientSecret))
            {
                plugin.ShowError("Enter the Google OAuth desktop client ID and client secret first.");
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

        private void ClearGoogleDriveFolder()
        {
            Settings.GoogleDriveFolderId = null;
            Settings.GoogleDriveFolderDisplayPath = null;
            OnPropertyChanged(nameof(GoogleDriveFolderSelectionStatus));
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
    }
}
