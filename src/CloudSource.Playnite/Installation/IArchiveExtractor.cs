using CloudSource.Playnite.Providers;
using System;
using System.Threading;

namespace CloudSource.Playnite.Installation
{
    internal interface IArchiveExtractor
    {
        SourcePackageKind Kind { get; }

        ExtractionResult Extract(
            string archivePath,
            string extractionRoot,
            Action<long, long> reportProgress,
            CancellationToken cancellationToken);
    }
}
