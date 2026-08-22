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

        Task<IReadOnlyList<SourcePackage>> ScanAsync(
            SourceScanRequest request,
            CancellationToken cancellationToken);

        Task<Stream> OpenReadAsync(
            SourcePackage package,
            CancellationToken cancellationToken);
    }
}
