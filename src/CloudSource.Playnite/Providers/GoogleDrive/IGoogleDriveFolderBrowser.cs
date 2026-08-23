using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal interface IGoogleDriveFolderBrowser
    {
        GoogleDriveFolder ProviderRoot { get; }
        IReadOnlyList<GoogleDriveFolder> GetDriveLocations();
        Task<IReadOnlyList<GoogleDriveFolder>> BrowseAsync(
            GoogleDriveAccountConfiguration configuration,
            GoogleDriveFolder parent,
            GoogleDriveAuthorization draftAuthorization,
            CancellationToken cancellationToken);
    }
}
