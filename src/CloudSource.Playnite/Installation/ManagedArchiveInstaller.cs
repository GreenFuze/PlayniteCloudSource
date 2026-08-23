using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Storage;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CloudSource.Playnite.Installation
{
    internal sealed class ManagedArchiveInstaller
    {
        private readonly Func<ManagedStorageLayout> layoutFactory;
        private readonly ProviderRegistry providerRegistry;
        private readonly ArchiveExtractorRegistry extractorRegistry;
        private readonly LaunchTargetResolver launchTargetResolver;
        private readonly InstallationManifestStore manifestStore;

        public ManagedArchiveInstaller(
            Func<ManagedStorageLayout> layoutFactory,
            ProviderRegistry providerRegistry,
            ArchiveExtractorRegistry extractorRegistry,
            LaunchTargetResolver launchTargetResolver,
            InstallationManifestStore manifestStore)
        {
            this.layoutFactory = layoutFactory ?? throw new ArgumentNullException(nameof(layoutFactory));
            this.providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
            this.extractorRegistry = extractorRegistry ?? throw new ArgumentNullException(nameof(extractorRegistry));
            this.launchTargetResolver = launchTargetResolver ?? throw new ArgumentNullException(nameof(launchTargetResolver));
            this.manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
        }

        public bool Supports(SourcePackageKind kind)
        {
            return extractorRegistry.Supports(kind);
        }

        public InstallationRecord Install(
            SourcePackage package,
            string gameName,
            Action<InstallationProgressUpdate> reportProgress,
            CancellationToken cancellationToken)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            var extractor = extractorRegistry.GetRequired(package.Kind);

            var existing = manifestStore.Find(package.StableId);
            if (existing != null) return existing;

            var layout = layoutFactory();
            layout.EnsureCreated();
            var stageRoot = Path.Combine(layout.StagingPath, "install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stageRoot);
            try
            {
                var archivePath = Path.Combine(stageRoot, "package" + GetArchiveExtension(package.Kind));
                reportProgress?.Invoke(new InstallationProgressUpdate(
                    InstallationProgressStage.Downloading,
                    totalBytes: package.SizeBytes));
                var download = Download(package, archivePath, reportProgress, cancellationToken);
                reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Extracting));
                var extraction = extractor.Extract(
                    archivePath,
                    Path.Combine(stageRoot, "content"),
                    cancellationToken);
                reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Finalizing));
                var launchTarget = launchTargetResolver.Resolve(extraction.PayloadRoot, gameName);
                var destination = SelectDestination(layout.GamesPath, gameName, package.StableId);
                var manifest = new InstallManifest
                {
                    GameId = package.StableId,
                    GameName = gameName,
                    ProviderId = package.ProviderId,
                    AccountId = package.AccountId,
                    ObjectId = package.ObjectId,
                    Revision = package.Revision,
                    LogicalPath = package.LogicalPath,
                    ArchiveSha256 = download.Sha256,
                    ArchiveSizeBytes = download.SizeBytes,
                    InstalledSizeBytes = extraction.ExpandedBytes,
                    LaunchTarget = launchTarget,
                    InstalledAtUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
                };
                manifestStore.Write(extraction.PayloadRoot, manifest);
                Directory.Move(extraction.PayloadRoot, destination);
                return new InstallationRecord(destination, manifest);
            }
            finally
            {
                if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, true);
            }
        }

        private static string GetArchiveExtension(SourcePackageKind kind)
        {
            switch (kind)
            {
                case SourcePackageKind.ZipArchive: return ".zip";
                case SourcePackageKind.SevenZipArchive: return ".7z";
                case SourcePackageKind.RarArchive: return ".rar";
                default: throw new NotSupportedException($"Cloud Storage cannot stage archive kind '{kind}'.");
            }
        }

        public void Uninstall(string gameId, string preferredDirectory)
        {
            var record = manifestStore.Find(gameId, preferredDirectory);
            if (record == null)
            {
                throw new InvalidDataException("No valid managed installation manifest was found. Nothing was deleted.");
            }

            if (!manifestStore.IsManagedGameDirectory(record.InstallDirectory))
            {
                throw new InvalidDataException("The installation directory is outside the managed Games directory.");
            }

            Directory.Delete(record.InstallDirectory, true);
        }

        private DownloadResult Download(
            SourcePackage package,
            string destination,
            Action<InstallationProgressUpdate> reportProgress,
            CancellationToken cancellationToken)
        {
            var provider = providerRegistry.GetRequired(package.ProviderId);
            using (var input = provider.OpenReadAsync(package, cancellationToken).GetAwaiter().GetResult())
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = SHA256.Create())
            {
                var buffer = new byte[128 * 1024];
                long size = 0;
                long nextProgressReport = 4L * 1024 * 1024;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    hash.TransformBlock(buffer, 0, read, null, 0);
                    size += read;
                    if (size >= nextProgressReport)
                    {
                        reportProgress?.Invoke(new InstallationProgressUpdate(
                            InstallationProgressStage.Downloading,
                            size,
                            package.SizeBytes));
                        nextProgressReport = size + (4L * 1024 * 1024);
                    }
                }

                reportProgress?.Invoke(new InstallationProgressUpdate(
                    InstallationProgressStage.Downloading,
                    size,
                    package.SizeBytes));

                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                if (package.SizeBytes > 0 && package.SizeBytes != size)
                {
                    throw new InvalidDataException($"Downloaded archive size mismatch: expected {package.SizeBytes}, received {size}.");
                }

                return new DownloadResult(size, BitConverter.ToString(hash.Hash).Replace("-", string.Empty).ToLowerInvariant());
            }
        }

        private static string SelectDestination(string gamesPath, string gameName, string stableId)
        {
            var safeName = SanitizeDirectoryName(gameName);
            var destination = Path.Combine(gamesPath, safeName);
            if (!Directory.Exists(destination) && !File.Exists(destination)) return destination;

            using (var sha = SHA256.Create())
            {
                var suffix = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(stableId)))
                    .Replace("-", string.Empty).Substring(0, 8).ToLowerInvariant();
                destination = Path.Combine(gamesPath, safeName + "-" + suffix);
            }

            if (Directory.Exists(destination) || File.Exists(destination))
            {
                throw new IOException($"Managed installation destination already exists: {destination}");
            }

            return destination;
        }

        private static string SanitizeDirectoryName(string gameName)
        {
            var invalid = new System.Collections.Generic.HashSet<char>(Path.GetInvalidFileNameChars());
            var safe = new string((gameName ?? string.Empty).Where(character => !invalid.Contains(character)).ToArray())
                .Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(safe) || safe == "." || safe == "..")
            {
                throw new InvalidDataException("The game name cannot be used as an installation directory.");
            }

            return safe;
        }

        private sealed class DownloadResult
        {
            public long SizeBytes { get; }
            public string Sha256 { get; }
            public DownloadResult(long sizeBytes, string sha256) { SizeBytes = sizeBytes; Sha256 = sha256; }
        }
    }
}
