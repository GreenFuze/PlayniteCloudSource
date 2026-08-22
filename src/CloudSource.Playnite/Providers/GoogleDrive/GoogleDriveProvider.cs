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

        private readonly Func<GoogleDriveAccountConfiguration> configurationFactory;
        private readonly Func<bool> isEnabled;
        private readonly GoogleDriveConnectionService connectionService;
        private readonly GoogleDriveApiClient apiClient;

        public string Id => ProviderId;
        public string Name => "Google Drive";
        public bool IsConfigured
        {
            get
            {
                if (!isEnabled() || !connectionService.HasStoredAuthorization)
                {
                    return false;
                }

                try
                {
                    return configurationFactory() != null;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
        }

        internal GoogleDriveProvider(
            Func<GoogleDriveAccountConfiguration> configurationFactory,
            Func<bool> isEnabled,
            GoogleDriveConnectionService connectionService,
            GoogleDriveApiClient apiClient)
        {
            this.configurationFactory = configurationFactory ?? throw new ArgumentNullException(nameof(configurationFactory));
            this.isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
            this.connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        public Task<IReadOnlyList<SourcePackage>> ScanAsync(
            SourceScanRequest request,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            return apiClient.ScanAsync(configurationFactory(), request, cancellationToken);
        }

        public Task<Stream> OpenReadAsync(
            SourcePackage package,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            return apiClient.OpenReadAsync(configurationFactory(), package, cancellationToken);
        }

        private void EnsureConfigured()
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("Google Drive is not configured.");
            }
        }
    }
}
