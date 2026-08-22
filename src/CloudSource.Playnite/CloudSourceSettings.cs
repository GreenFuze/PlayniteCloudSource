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
            "Cloud Source");
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

        public GoogleDriveAccountConfiguration CreateGoogleDriveConfiguration()
        {
            return new GoogleDriveAccountConfiguration(
                GoogleDriveClientId,
                GoogleDriveClientSecret,
                GoogleDriveAccountId,
                GoogleDriveAccountDisplayName);
        }
    }

    public sealed class CloudSourceSettingsViewModel : ObservableObject, ISettings
    {
        private readonly CloudSourcePlugin plugin;
        private CloudSourceSettings editingClone;
        private CloudSourceSettings settings;
        private GoogleDriveAuthorization pendingGoogleDriveAuthorization;
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

        public RelayCommand ConnectGoogleDriveCommand { get; }
        public RelayCommand DisconnectGoogleDriveCommand { get; }

        public CloudSourceSettingsViewModel(CloudSourcePlugin plugin)
        {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            Settings = plugin.LoadPluginSettings<CloudSourceSettings>() ?? new CloudSourceSettings();
            ConnectGoogleDriveCommand = new RelayCommand(ConnectGoogleDrive);
            DisconnectGoogleDriveCommand = new RelayCommand(DisconnectGoogleDrive);
            RefreshGoogleDriveStatus();
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
            pendingGoogleDriveAuthorization = null;
            pendingGoogleDriveDisconnect = false;
            RefreshGoogleDriveStatus();
        }

        public void CancelEdit()
        {
            Settings = editingClone ?? new CloudSourceSettings();
            pendingGoogleDriveAuthorization = null;
            pendingGoogleDriveDisconnect = false;
            RefreshGoogleDriveStatus();
        }

        public void EndEdit()
        {
            if (!ManagedStorageLayout.TryCreate(Settings.ManagedRootPath, out var layout, out var error))
            {
                throw new InvalidOperationException(error);
            }

            layout.EnsureCreated();
            Settings.ManagedRootPath = layout.RootPath;

            if (pendingGoogleDriveAuthorization != null)
            {
                plugin.CommitGoogleDriveAuthorization(pendingGoogleDriveAuthorization);
                plugin.SavePluginSettings(Settings);
            }
            else if (pendingGoogleDriveDisconnect)
            {
                plugin.SavePluginSettings(Settings);
                plugin.DisconnectGoogleDrive();
            }
            else
            {
                plugin.SavePluginSettings(Settings);
            }

            pendingGoogleDriveAuthorization = null;
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

                if (string.IsNullOrWhiteSpace(Settings.GoogleDriveFolderId) ||
                    string.IsNullOrWhiteSpace(Settings.GoogleDriveFolderDisplayPath))
                {
                    errors.Add("Google Drive source folder ID and display path are required.");
                }

                if (pendingGoogleDriveAuthorization == null && !plugin.HasGoogleDriveAuthorization)
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
                var authorization = await plugin.AuthorizeGoogleDriveAsync(
                    Settings.GoogleDriveClientId.Trim(),
                    Settings.GoogleDriveClientSecret.Trim(),
                    CancellationToken.None);
                pendingGoogleDriveAuthorization = authorization;
                pendingGoogleDriveDisconnect = false;
                Settings.GoogleDriveEnabled = true;
                Settings.GoogleDriveAccountId = authorization.AccountId;
                Settings.GoogleDriveAccountDisplayName = authorization.AccountDisplayName;
                GoogleDriveStatus = $"Connected draft: {authorization.AccountDisplayName}. Save settings to commit.";
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

            pendingGoogleDriveAuthorization = null;
            pendingGoogleDriveDisconnect = true;
            Settings.GoogleDriveEnabled = false;
            Settings.GoogleDriveAccountId = null;
            Settings.GoogleDriveAccountDisplayName = null;
            GoogleDriveStatus = "Disconnected draft. Save settings to remove the stored authorization.";
        }

        private void RefreshGoogleDriveStatus()
        {
            if (Settings.GoogleDriveEnabled &&
                plugin.HasGoogleDriveAuthorization &&
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
