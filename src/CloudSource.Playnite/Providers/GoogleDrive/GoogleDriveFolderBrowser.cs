using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal sealed class GoogleDriveFolderBrowser : IGoogleDriveFolderBrowser
    {
        private readonly GoogleDriveApiClient apiClient;

        public GoogleDriveFolder ProviderRoot { get; } = GoogleDriveFolder.ProviderRoot();

        public GoogleDriveFolderBrowser(GoogleDriveApiClient apiClient)
        {
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        public IReadOnlyList<GoogleDriveFolder> GetDriveLocations()
        {
            return new[]
            {
                GoogleDriveFolder.MyDrive(),
                GoogleDriveFolder.SharedWithMe()
            };
        }

        public Task<IReadOnlyList<GoogleDriveFolder>> BrowseAsync(
            GoogleDriveAccountConfiguration configuration,
            GoogleDriveFolder parent,
            GoogleDriveAuthorization draftAuthorization,
            CancellationToken cancellationToken)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            return apiClient.ListFoldersAsync(
                configuration,
                parent,
                draftAuthorization,
                cancellationToken);
        }
    }
}
