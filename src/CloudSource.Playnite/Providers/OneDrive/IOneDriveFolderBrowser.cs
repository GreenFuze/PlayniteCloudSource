using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Providers.OneDrive
{
    internal interface IOneDriveFolderBrowser
    {
        OneDriveFolder ProviderRoot { get; }
        IReadOnlyList<OneDriveFolder> GetDriveLocations();
        Task<IReadOnlyList<OneDriveFolder>> BrowseAsync(
            OneDriveAccountConfiguration configuration,
            OneDriveFolder parent,
            OneDriveAuthorization draftAuthorization,
            CancellationToken cancellationToken);
    }
}
