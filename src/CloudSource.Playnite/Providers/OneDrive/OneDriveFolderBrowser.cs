using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Providers.OneDrive
{
    internal sealed class OneDriveFolderBrowser : IOneDriveFolderBrowser
    {
        private readonly OneDriveApiClient apiClient;

        public OneDriveFolder ProviderRoot { get; } = OneDriveFolder.ProviderRoot();

        public OneDriveFolderBrowser(OneDriveApiClient apiClient)
        {
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        public IReadOnlyList<OneDriveFolder> GetDriveLocations()
        {
            return new[] { OneDriveFolder.MyFiles() };
        }

        public Task<IReadOnlyList<OneDriveFolder>> BrowseAsync(
            OneDriveAccountConfiguration configuration,
            OneDriveFolder parent,
            OneDriveAuthorization draftAuthorization,
            CancellationToken cancellationToken)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            return apiClient.ListFoldersAsync(configuration, parent, draftAuthorization, cancellationToken);
        }
    }
}
