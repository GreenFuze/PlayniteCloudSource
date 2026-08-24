using CloudSource.Playnite.Emulation;
using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Storage;
using Playnite.SDK;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CloudSource.Playnite.Installation
{
    internal sealed class ManagedRomInstaller
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly Func<ManagedStorageLayout> layoutFactory;
        private readonly ProviderRegistry providerRegistry;
        private readonly InstallationManifestStore manifestStore;

        public ManagedRomInstaller(
            Func<ManagedStorageLayout> layoutFactory,
            ProviderRegistry providerRegistry,
            InstallationManifestStore manifestStore)
        {
            this.layoutFactory = layoutFactory ?? throw new ArgumentNullException(nameof(layoutFactory));
            this.providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
            this.manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
        }

        public InstallationRecord Install(
            SourcePackage package,
            string gameName,
            EmulatorInstallPlan emulatorPlan,
            Action<InstallationProgressUpdate> reportProgress,
            CancellationToken cancellationToken)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (emulatorPlan == null) throw new ArgumentNullException(nameof(emulatorPlan));
            var existing = manifestStore.Find(package.StableId);
            if (existing != null) return existing;

            var primary = package.Files.Single(file => file.Role == SourcePackageFileRole.Primary);
            if (Path.GetFileName(primary.DisplayName) != primary.DisplayName)
                throw new InvalidDataException($"ROM package contains an unsafe filename: {primary.DisplayName}");

            var layout = layoutFactory();
            layout.EnsureCreated();
            var destination = SelectDestination(layout.GamesPath, gameName, package.StableId);
            var stageRoot = Path.Combine(layout.StagingPath, "rom-" + Guid.NewGuid().ToString("N"));
            var contentRoot = Path.Combine(stageRoot, "content");
            Directory.CreateDirectory(contentRoot);
            try
            {
                var romPath = Path.Combine(contentRoot, primary.DisplayName);
                var download = Download(package, primary, romPath, reportProgress, cancellationToken);
                reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Finalizing, 0, 1));
                var manifest = new InstallManifest
                {
                    SchemaVersion = 3,
                    GameId = package.StableId,
                    GameName = gameName,
                    ProviderId = package.ProviderId,
                    AccountId = package.AccountId,
                    ObjectId = package.ObjectId,
                    Revision = package.Revision,
                    LogicalPath = package.LogicalPath,
                    ArchiveSha256 = download.Sha256,
                    ArchiveSizeBytes = download.SizeBytes,
                    InstalledSizeBytes = download.SizeBytes,
                    InstallKind = "managed_rom",
                    RomTarget = primary.DisplayName,
                    PlatformSpecificationId = emulatorPlan.PlatformSpecificationId,
                    EmulatorId = emulatorPlan.EmulatorId.ToString("D"),
                    EmulatorProfileId = emulatorPlan.EmulatorProfileId,
                    InstalledAtUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
                };
                manifestStore.Write(contentRoot, manifest);
                Directory.Move(contentRoot, destination);
                Logger.Info($"Cloud Storage ROM installation completed for '{gameName}' at '{destination}' without extraction.");
                reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Finalizing, 1, 1));
                return new InstallationRecord(destination, manifest);
            }
            finally
            {
                if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, true);
            }
        }

        private DownloadResult Download(
            SourcePackage package,
            SourcePackageFile file,
            string destination,
            Action<InstallationProgressUpdate> reportProgress,
            CancellationToken cancellationToken)
        {
            var provider = providerRegistry.GetRequired(package.ProviderId);
            using (var input = provider.OpenReadFileAsync(package, file, cancellationToken).GetAwaiter().GetResult())
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = SHA256.Create())
            {
                var total = file.SizeBytes > 0 ? file.SizeBytes : ResolveLength(input);
                reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Downloading, 0, total));
                var buffer = new byte[128 * 1024];
                long size = 0;
                long nextReport = 4L * 1024 * 1024;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    hash.TransformBlock(buffer, 0, read, null, 0);
                    size += read;
                    if (size >= nextReport)
                    {
                        reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Downloading, size, total));
                        nextReport = size + (4L * 1024 * 1024);
                    }
                }

                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Downloading, size, total));
                if (file.SizeBytes > 0 && file.SizeBytes != size)
                    throw new InvalidDataException($"Downloaded ROM size mismatch: expected {file.SizeBytes}, received {size}.");
                return new DownloadResult(size, BitConverter.ToString(hash.Hash).Replace("-", string.Empty).ToLowerInvariant());
            }
        }

        private static long ResolveLength(Stream input)
        {
            if (input is IKnownLengthStream known && known.ContentLength.GetValueOrDefault() > 0)
                return known.ContentLength.Value;
            return input.CanSeek ? input.Length : 0;
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
                throw new IOException($"Managed installation destination already exists: {destination}");
            return destination;
        }

        private static string SanitizeDirectoryName(string gameName)
        {
            var invalid = new System.Collections.Generic.HashSet<char>(Path.GetInvalidFileNameChars());
            var safe = new string((gameName ?? string.Empty).Where(character => !invalid.Contains(character)).ToArray())
                .Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(safe) || safe == "." || safe == "..")
                throw new InvalidDataException("The game name cannot be used as an installation directory.");
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
