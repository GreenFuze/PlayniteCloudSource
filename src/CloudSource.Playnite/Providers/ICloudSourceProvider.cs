using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Providers
{
    public interface ICloudSourceProvider
    {
        string Id { get; }
        string Name { get; }
        bool IsConfigured { get; }
        bool HasStoredConnection { get; }
        bool HasPendingConnection { get; }

        Task<CloudProviderAccount> ConnectAsync(CancellationToken cancellationToken);
        void CommitPendingConnection();
        void DiscardPendingConnection();
        void Disconnect();
        CloudProviderFolder SelectSourceFolder(string existingSelectionPath);

        Task<IReadOnlyList<CloudProviderScanResult>> ScanAsync(
            CancellationToken cancellationToken);

        Task<Stream> OpenReadAsync(
            SourcePackage package,
            CancellationToken cancellationToken);

        Task<Stream> OpenReadFileAsync(
            SourcePackage package,
            SourcePackageFile file,
            CancellationToken cancellationToken);
    }
}
