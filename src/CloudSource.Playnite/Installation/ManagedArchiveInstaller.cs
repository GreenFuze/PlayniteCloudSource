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
    internal sealed class ManagedArchiveInstaller
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly Func<ManagedStorageLayout> layoutFactory;
        private readonly ProviderRegistry providerRegistry;
        private readonly ArchiveExtractorRegistry extractorRegistry;
        private readonly LaunchTargetResolver launchTargetResolver;
        private readonly InstallationManifestStore manifestStore;
        private readonly InstallerPackageClassifier packageClassifier;
        private readonly NativeInnoInstaller nativeInnoInstaller;

        public ManagedArchiveInstaller(
            Func<ManagedStorageLayout> layoutFactory,
            ProviderRegistry providerRegistry,
            ArchiveExtractorRegistry extractorRegistry,
            LaunchTargetResolver launchTargetResolver,
            InstallationManifestStore manifestStore)
            : this(
                layoutFactory,
                providerRegistry,
                extractorRegistry,
                launchTargetResolver,
                manifestStore,
                new InstallerPackageClassifier(),
                new NativeInnoInstaller(launchTargetResolver))
        {
        }

        internal ManagedArchiveInstaller(
            Func<ManagedStorageLayout> layoutFactory,
            ProviderRegistry providerRegistry,
            ArchiveExtractorRegistry extractorRegistry,
            LaunchTargetResolver launchTargetResolver,
            InstallationManifestStore manifestStore,
            InstallerPackageClassifier packageClassifier,
            NativeInnoInstaller nativeInnoInstaller)
        {
            this.layoutFactory = layoutFactory ?? throw new ArgumentNullException(nameof(layoutFactory));
            this.providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
            this.extractorRegistry = extractorRegistry ?? throw new ArgumentNullException(nameof(extractorRegistry));
            this.launchTargetResolver = launchTargetResolver ?? throw new ArgumentNullException(nameof(launchTargetResolver));
            this.manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
            this.packageClassifier = packageClassifier ?? throw new ArgumentNullException(nameof(packageClassifier));
            this.nativeInnoInstaller = nativeInnoInstaller ?? throw new ArgumentNullException(nameof(nativeInnoInstaller));
        }

        public bool Supports(SourcePackageKind kind)
        {
            return kind == SourcePackageKind.InnoInstallerBundle || extractorRegistry.Supports(kind);
        }

        public InstallationRecord Install(
            SourcePackage package,
            string gameName,
            Func<InstallerConfirmationRequest, bool> confirmInstaller,
            Func<LaunchTargetSelectionRequest, string> selectLaunchTarget,
            Action<InstallationProgressUpdate> reportProgress,
            CancellationToken cancellationToken)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            var existing = manifestStore.Find(package.StableId);
            if (existing != null) return existing;

            Logger.Info($"Cloud Storage installation started for '{gameName}' ({package.Kind}, {package.StableId}).");

            var layout = layoutFactory();
            layout.EnsureCreated();
            var recovered = TryFinalizeExistingNativeInstallation(
                package,
                gameName,
                layout.GamesPath,
                selectLaunchTarget,
                reportProgress);
            if (recovered != null)
            {
                return recovered;
            }

            var stageRoot = Path.Combine(layout.StagingPath, "install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stageRoot);
            try
            {
                reportProgress?.Invoke(new InstallationProgressUpdate(
                    InstallationProgressStage.Downloading,
                    totalBytes: package.SizeBytes));
                var prepared = Prepare(package, stageRoot, reportProgress, cancellationToken);
                var classification = packageClassifier.Classify(prepared.PayloadRoot);
                Logger.Info($"Cloud Storage prepared '{gameName}' as {classification.Kind}.");
                var destination = SelectDestination(layout.GamesPath, gameName, package.StableId);
                if (classification.Kind == PreparedPayloadKind.InnoInstaller)
                {
                    return InstallNativeInno(
                        package,
                        gameName,
                        destination,
                        prepared,
                        classification,
                        confirmInstaller,
                        selectLaunchTarget,
                        reportProgress,
                        cancellationToken);
                }

                if (package.Kind == SourcePackageKind.InnoInstallerBundle)
                {
                    throw new InvalidDataException("The executable package is not a supported Inno Setup installer.");
                }

                reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Finalizing, 0, 1));
                var launchTarget = launchTargetResolver.Resolve(prepared.PayloadRoot, gameName, selectLaunchTarget);
                var manifest = new InstallManifest
                {
                    GameId = package.StableId,
                    GameName = gameName,
                    ProviderId = package.ProviderId,
                    AccountId = package.AccountId,
                    ObjectId = package.ObjectId,
                    Revision = package.Revision,
                    LogicalPath = package.LogicalPath,
                    ArchiveSha256 = prepared.PrimaryDownload.Sha256,
                    ArchiveSizeBytes = prepared.TotalDownloadedBytes,
                    InstalledSizeBytes = prepared.ExpandedBytes,
                    LaunchTarget = launchTarget,
                    InstallKind = "managed_archive",
                    InstalledAtUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
                };
                manifestStore.Write(prepared.PayloadRoot, manifest);
                Directory.Move(prepared.PayloadRoot, destination);
                Logger.Info($"Cloud Storage portable installation completed for '{gameName}' at '{destination}'.");
                reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Finalizing, 1, 1));
                return new InstallationRecord(destination, manifest);
            }
            finally
            {
                if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, true);
            }
        }

        private PreparedPayload Prepare(
            SourcePackage package,
            string stageRoot,
            Action<InstallationProgressUpdate> reportProgress,
            CancellationToken cancellationToken)
        {
            if (package.Kind == SourcePackageKind.InnoInstallerBundle)
            {
                var contentRoot = Path.Combine(stageRoot, "content");
                Directory.CreateDirectory(contentRoot);
                var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long completed = 0;
                DownloadResult primary = null;
                foreach (var file in package.Files.OrderBy(file => file.Role))
                {
                    if (Path.GetFileName(file.DisplayName) != file.DisplayName || !names.Add(file.DisplayName))
                        throw new InvalidDataException($"Installer package contains an unsafe or duplicate filename: {file.DisplayName}");
                    var result = DownloadFile(
                        package,
                        file,
                        Path.Combine(contentRoot, file.DisplayName),
                        completed,
                        package.SizeBytes,
                        reportProgress,
                        cancellationToken);
                    completed += result.SizeBytes;
                    if (file.Role == SourcePackageFileRole.Primary) primary = result;
                }

                return new PreparedPayload(contentRoot, completed, completed, primary);
            }

            var extractor = extractorRegistry.GetRequired(package.Kind);
            var primaryFile = package.Files.Single(file => file.Role == SourcePackageFileRole.Primary);
            var archivePath = Path.Combine(stageRoot, "package" + GetArchiveExtension(package.Kind));
            var download = DownloadFile(
                package,
                primaryFile,
                archivePath,
                0,
                primaryFile.SizeBytes,
                reportProgress,
                cancellationToken);
            var extraction = extractor.Extract(
                archivePath,
                Path.Combine(stageRoot, "content"),
                (completed, total) => reportProgress?.Invoke(new InstallationProgressUpdate(
                    InstallationProgressStage.Extracting,
                    completed,
                    total)),
                cancellationToken);
            return new PreparedPayload(extraction.PayloadRoot, extraction.ExpandedBytes, download.SizeBytes, download);
        }

        private InstallationRecord InstallNativeInno(
            SourcePackage package,
            string gameName,
            string destination,
            PreparedPayload prepared,
            PreparedPayloadClassification classification,
            Func<InstallerConfirmationRequest, bool> confirmInstaller,
            Func<LaunchTargetSelectionRequest, string> selectLaunchTarget,
            Action<InstallationProgressUpdate> reportProgress,
            CancellationToken cancellationToken)
        {
            Logger.Info($"Cloud Storage is starting the confirmed Inno installer for '{gameName}' at '{destination}'.");
            var native = nativeInnoInstaller.Install(
                classification,
                gameName,
                destination,
                confirmInstaller,
                selectLaunchTarget,
                reportProgress,
                cancellationToken);
            reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Finalizing, 0, 1));
            var manifest = CreateInnoManifest(
                package,
                gameName,
                destination,
                native,
                prepared.PrimaryDownload.Sha256,
                prepared.TotalDownloadedBytes,
                Path.GetFileName(classification.InstallerPath),
                classification.SignerSubject,
                "confirmed_interactive");
            manifestStore.Write(destination, manifest);
            Logger.Info($"Cloud Storage Inno installation completed for '{gameName}' at '{destination}'.");
            reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Finalizing, 1, 1));
            return new InstallationRecord(destination, manifest);
        }

        private InstallationRecord TryFinalizeExistingNativeInstallation(
            SourcePackage package,
            string gameName,
            string gamesPath,
            Func<LaunchTargetSelectionRequest, string> selectLaunchTarget,
            Action<InstallationProgressUpdate> reportProgress)
        {
            var destination = GetPrimaryDestination(gamesPath, gameName);
            if (!Directory.Exists(destination) ||
                File.Exists(Path.Combine(destination, InstallationManifestStore.FileName)) ||
                !nativeInnoInstaller.CanFinalizeExistingInstallation(destination))
            {
                return null;
            }

            Logger.Info($"Cloud Storage found a completed native installation for '{gameName}' at '{destination}' and will finalize it without rerunning setup.");
            var native = nativeInnoInstaller.FinalizeExistingInstallation(
                gameName,
                destination,
                selectLaunchTarget,
                reportProgress);
            reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Finalizing, 0, 1));
            var manifest = CreateInnoManifest(
                package,
                gameName,
                destination,
                native,
                null,
                package.SizeBytes,
                package.DisplayName,
                null,
                "recovered_existing_post_install");
            manifestStore.Write(destination, manifest);
            Logger.Info($"Cloud Storage finalized the existing native installation for '{gameName}'.");
            reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Finalizing, 1, 1));
            return new InstallationRecord(destination, manifest);
        }

        private static InstallManifest CreateInnoManifest(
            SourcePackage package,
            string gameName,
            string destination,
            NativeInnoInstallResult native,
            string archiveSha256,
            long archiveSizeBytes,
            string installerFileName,
            string signerSubject,
            string invocationMode)
        {
            var installedSize = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
            return new InstallManifest
            {
                GameId = package.StableId,
                GameName = gameName,
                ProviderId = package.ProviderId,
                AccountId = package.AccountId,
                ObjectId = package.ObjectId,
                Revision = package.Revision,
                LogicalPath = package.LogicalPath,
                ArchiveSha256 = archiveSha256,
                ArchiveSizeBytes = archiveSizeBytes,
                InstalledSizeBytes = installedSize,
                LaunchTarget = native.LaunchTarget,
                InstallKind = "inno",
                InstallerFamily = "inno_setup",
                InstallerFileName = installerFileName,
                SignerSubject = signerSubject,
                InvocationMode = invocationMode,
                UninstallTarget = native.UninstallTarget,
                InstalledAtUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
            };
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

        public ManagedUninstallResult Uninstall(string gameId, string preferredDirectory)
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

            if (string.Equals(record.Manifest.InstallKind, "inno", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(record.Manifest.UninstallTarget))
                    throw new InvalidDataException("The native installation manifest has no recorded uninstaller.");
                nativeInnoInstaller.Uninstall(record.InstallDirectory, record.Manifest.UninstallTarget);
                manifestStore.RemoveManifest(record.InstallDirectory);
                Logger.Info($"Cloud Storage Inno uninstall completed for '{record.Manifest.GameName}'.");
                return new ManagedUninstallResult(
                    record.InstallDirectory,
                    Directory.Exists(record.InstallDirectory));
            }

            Directory.Delete(record.InstallDirectory, true);
            return new ManagedUninstallResult(record.InstallDirectory, false);
        }

        public bool CanCompleteExtractedInstaller(string gameId, string preferredDirectory)
        {
            var record = manifestStore.Find(gameId, preferredDirectory);
            if (record == null || record.Manifest.SchemaVersion != 1) return false;
            try
            {
                return packageClassifier.Classify(record.InstallDirectory).Kind == PreparedPayloadKind.InnoInstaller;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is CryptographicException)
            {
                return false;
            }
        }

        public InstallationRecord CompleteExtractedInstaller(
            string gameId,
            string preferredDirectory,
            string gameName,
            Func<InstallerConfirmationRequest, bool> confirmInstaller,
            Func<LaunchTargetSelectionRequest, string> selectLaunchTarget,
            Action<InstallationProgressUpdate> reportProgress,
            CancellationToken cancellationToken)
        {
            var existing = manifestStore.Find(gameId, preferredDirectory);
            if (existing == null || existing.Manifest.SchemaVersion != 1)
                throw new InvalidDataException("No legacy extracted installer package is available to complete.");
            var classification = packageClassifier.Classify(existing.InstallDirectory);
            if (classification.Kind != PreparedPayloadKind.InnoInstaller)
                throw new InvalidDataException("The existing managed folder is not an Inno installer package.");

            var layout = layoutFactory();
            layout.EnsureCreated();
            var destination = SelectDestination(layout.GamesPath, gameName, gameId);
            Logger.Info($"Cloud Storage is completing the extracted Inno package for '{gameName}' at '{destination}'.");
            var native = nativeInnoInstaller.Install(
                classification,
                gameName,
                destination,
                confirmInstaller,
                selectLaunchTarget,
                reportProgress,
                cancellationToken);
            reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Finalizing, 0, 1));
            var installedSize = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
            var manifest = new InstallManifest
            {
                GameId = existing.Manifest.GameId,
                GameName = gameName,
                ProviderId = existing.Manifest.ProviderId,
                AccountId = existing.Manifest.AccountId,
                ObjectId = existing.Manifest.ObjectId,
                Revision = existing.Manifest.Revision,
                LogicalPath = existing.Manifest.LogicalPath,
                ArchiveSha256 = existing.Manifest.ArchiveSha256,
                ArchiveSizeBytes = existing.Manifest.ArchiveSizeBytes,
                InstalledSizeBytes = installedSize,
                LaunchTarget = native.LaunchTarget,
                InstallKind = "inno",
                InstallerFamily = "inno_setup",
                InstallerFileName = Path.GetFileName(classification.InstallerPath),
                SignerSubject = classification.SignerSubject,
                InvocationMode = "confirmed_interactive",
                UninstallTarget = native.UninstallTarget,
                InstalledAtUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
            };
            manifestStore.Write(destination, manifest);
            Directory.Delete(existing.InstallDirectory, true);
            Logger.Info($"Cloud Storage extracted Inno package completion succeeded for '{gameName}'.");
            reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.Finalizing, 1, 1));
            return new InstallationRecord(destination, manifest);
        }

        private DownloadResult DownloadFile(
            SourcePackage package,
            SourcePackageFile file,
            string destination,
            long completedBeforeFile,
            long aggregateTotal,
            Action<InstallationProgressUpdate> reportProgress,
            CancellationToken cancellationToken)
        {
            var provider = providerRegistry.GetRequired(package.ProviderId);
            using (var input = provider.OpenReadFileAsync(package, file, cancellationToken).GetAwaiter().GetResult())
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = SHA256.Create())
            {
                var fileTotalBytes = ResolveContentLength(input, file.SizeBytes);
                var totalBytes = aggregateTotal > 0 ? aggregateTotal : fileTotalBytes;
                reportProgress?.Invoke(new InstallationProgressUpdate(
                    InstallationProgressStage.Downloading,
                    completedBeforeFile,
                    totalBytes: totalBytes));
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
                            completedBeforeFile + size,
                            totalBytes));
                        nextProgressReport = size + (4L * 1024 * 1024);
                    }
                }

                reportProgress?.Invoke(new InstallationProgressUpdate(
                    InstallationProgressStage.Downloading,
                    completedBeforeFile + size,
                    totalBytes));

                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                if (fileTotalBytes > 0 && fileTotalBytes != size)
                {
                    throw new InvalidDataException($"Downloaded package file size mismatch: expected {fileTotalBytes}, received {size}.");
                }

                return new DownloadResult(size, BitConverter.ToString(hash.Hash).Replace("-", string.Empty).ToLowerInvariant());
            }
        }

        private static long ResolveContentLength(Stream input, long packageSize)
        {
            if (packageSize > 0)
            {
                return packageSize;
            }

            if (input is IKnownLengthStream knownLength && knownLength.ContentLength.GetValueOrDefault() > 0)
            {
                return knownLength.ContentLength.Value;
            }

            if (input.CanSeek)
            {
                return input.Length;
            }

            return 0;
        }

        private static string SelectDestination(string gamesPath, string gameName, string stableId)
        {
            var safeName = SanitizeDirectoryName(gameName);
            var destination = GetPrimaryDestination(gamesPath, gameName);
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

        private static string GetPrimaryDestination(string gamesPath, string gameName)
        {
            return Path.Combine(gamesPath, SanitizeDirectoryName(gameName));
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

        private sealed class PreparedPayload
        {
            public string PayloadRoot { get; }
            public long ExpandedBytes { get; }
            public long TotalDownloadedBytes { get; }
            public DownloadResult PrimaryDownload { get; }

            public PreparedPayload(string payloadRoot, long expandedBytes, long totalDownloadedBytes, DownloadResult primaryDownload)
            {
                PayloadRoot = payloadRoot ?? throw new ArgumentNullException(nameof(payloadRoot));
                ExpandedBytes = expandedBytes;
                TotalDownloadedBytes = totalDownloadedBytes;
                PrimaryDownload = primaryDownload ?? throw new ArgumentNullException(nameof(primaryDownload));
            }
        }
    }

    internal sealed class ManagedUninstallResult
    {
        public string InstallDirectory { get; }
        public bool RemainingFilesPreserved { get; }

        public ManagedUninstallResult(string installDirectory, bool remainingFilesPreserved)
        {
            InstallDirectory = installDirectory ?? throw new ArgumentNullException(nameof(installDirectory));
            RemainingFilesPreserved = remainingFilesPreserved;
        }
    }
}
