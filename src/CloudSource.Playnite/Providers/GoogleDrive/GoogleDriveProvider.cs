using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    public sealed class GoogleDriveProvider : ICloudSourceProvider
    {
        public const string ProviderId = "google-drive";

        private readonly Func<GoogleDriveProviderConfiguration> configurationFactory;
        private readonly string clientId;
        private readonly GoogleDriveConnectionService connectionService;
        private readonly GoogleDriveApiClient apiClient;
        private readonly global::CloudSource.Playnite.GoogleDriveFolderPickerDialog folderPicker;
        private GoogleDriveAuthorization pendingAuthorization;

        public string Id => ProviderId;
        public string Name => "Google Drive";
        public bool HasStoredConnection => connectionService.HasStoredAuthorization;
        public bool HasPendingConnection => pendingAuthorization != null;
        public bool IsConfigured
        {
            get
            {
                try
                {
                    var configuration = configurationFactory();
                    if (!configuration.Enabled || !configuration.HasConcreteFolder || !HasStoredConnection)
                        return false;
                    configuration.CreateAccountConfiguration(clientId);
                    configuration.CreateScanRequest();
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
        }

        internal GoogleDriveProvider(
            Func<GoogleDriveProviderConfiguration> configurationFactory,
            string clientId,
            GoogleDriveConnectionService connectionService,
            GoogleDriveApiClient apiClient,
            global::CloudSource.Playnite.GoogleDriveFolderPickerDialog folderPicker)
        {
            this.configurationFactory = configurationFactory ?? throw new ArgumentNullException(nameof(configurationFactory));
            this.clientId = clientId?.Trim();
            this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            this.folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        }

        public async Task<CloudProviderAccount> ConnectAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("This Cloud Storage build has no Google application registration configured.");

            pendingAuthorization = null;
            connectionService.ClearIncompatibleAuthorization();
            pendingAuthorization = await connectionService.AuthorizeAsync(
                clientId,
                cancellationToken).ConfigureAwait(false);
            return new CloudProviderAccount(
                pendingAuthorization.AccountId,
                pendingAuthorization.AccountDisplayName);
        }

        public void CommitPendingConnection()
        {
            if (pendingAuthorization == null)
                throw new InvalidOperationException("Google Drive has no pending connection to commit.");
            connectionService.Commit(pendingAuthorization);
            pendingAuthorization = null;
        }

        public void DiscardPendingConnection()
        {
            pendingAuthorization = null;
        }

        public void Disconnect()
        {
            pendingAuthorization = null;
            connectionService.Disconnect();
        }

        public CloudProviderFolder SelectSourceFolder(string existingSelectionPath)
        {
            EnsureConnected();
            var selected = folderPicker.Show(
                configurationFactory().CreateAccountConfiguration(clientId),
                pendingAuthorization,
                existingSelectionPath);
            return selected == null ? null : new CloudProviderFolder(selected.ObjectId, selected.DisplayPath);
        }

        public async Task<IReadOnlyList<CloudProviderScanResult>> ScanAsync(CancellationToken cancellationToken)
        {
            EnsureConfigured();
            var configuration = configurationFactory();
            var packages = await apiClient.ScanAsync(
                configuration.CreateAccountConfiguration(clientId),
                configuration.CreateScanRequest(),
                cancellationToken).ConfigureAwait(false);
            return new[] { new CloudProviderScanResult(Id, configuration.AccountId, packages) };
        }

        public Task<Stream> OpenReadAsync(
            SourcePackage package,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            return apiClient.OpenReadAsync(
                configurationFactory().CreateAccountConfiguration(clientId),
                package,
                cancellationToken);
        }

        public Task<Stream> OpenReadFileAsync(
            SourcePackage package,
            SourcePackageFile file,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            return apiClient.OpenReadFileAsync(
                configurationFactory().CreateAccountConfiguration(clientId),
                package,
                file,
                cancellationToken);
        }

        private void EnsureConfigured()
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("Google Drive is not configured.");
            }
        }

        private void EnsureConnected()
        {
            if (!HasStoredConnection && !HasPendingConnection)
                throw new InvalidOperationException("Google Drive is not connected.");
            configurationFactory().CreateAccountConfiguration(clientId);
        }
    }
}
