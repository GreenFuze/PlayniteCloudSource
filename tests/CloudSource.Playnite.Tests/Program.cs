using CloudSource.Playnite.GameImport;
using CloudSource.Playnite.Installation;
using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace CloudSource.Playnite.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var root = Path.Combine(Path.GetTempPath(), "cloud-storage-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                ExtractsWrapperAndSelectsGameExecutable(root);
                RejectsTraversal(root);
                ExtractsSevenZipAndRar(root);
                RegistersEverySupportedArchiveKind();
                ReportsInstallationPhases(root);
                RejectsRarLinks(root);
                DeletesOnlyManifestValidatedManagedInstallations(root);
                ReconcilesOnlyAnAuthoritativeProviderAccountScope();
                Console.WriteLine("All Cloud Storage tests passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void RegistersEverySupportedArchiveKind()
        {
            var registry = new ArchiveExtractorRegistry(new IArchiveExtractor[]
            {
                new SafeZipExtractor(),
                new SafeSharpCompressExtractor(SourcePackageKind.SevenZipArchive),
                new SafeSharpCompressExtractor(SourcePackageKind.RarArchive)
            });

            foreach (SourcePackageKind kind in Enum.GetValues(typeof(SourcePackageKind)))
            {
                Assert(registry.Supports(kind), $"Archive kind '{kind}' has no registered installer.");
            }
        }

        private static void ReportsInstallationPhases(string root)
        {
            var archivePath = Path.Combine(root, "progress.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "Progress Game.exe", "game");
            }

            var packageBytes = File.ReadAllBytes(archivePath);
            var package = new SourcePackage(
                "test-provider",
                "account",
                "object",
                "revision",
                "Games/Progress Game.zip",
                "Progress Game.zip",
                packageBytes.Length,
                null,
                SourcePackageKind.ZipArchive);
            var managedRoot = Path.Combine(root, "progress-managed");
            Assert(ManagedStorageLayout.TryCreate(managedRoot, out var layout, out var error), error);
            var manifestStore = new InstallationManifestStore(() => layout);
            var installer = new ManagedArchiveInstaller(
                () => layout,
                new ProviderRegistry(new ICloudSourceProvider[] { new MemoryProvider(packageBytes) }),
                new ArchiveExtractorRegistry(new IArchiveExtractor[] { new SafeZipExtractor() }),
                new LaunchTargetResolver(new GameTitleNormalizer()),
                manifestStore);
            var updates = new List<InstallationProgressUpdate>();

            var record = installer.Install(package, "Progress Game", updates.Add, CancellationToken.None);

            Assert(Directory.Exists(record.InstallDirectory), "Progress test game was not installed.");
            Assert(
                updates.Select(update => update.Stage).Distinct().SequenceEqual(new[]
                {
                    InstallationProgressStage.Downloading,
                    InstallationProgressStage.Extracting,
                    InstallationProgressStage.Finalizing
                }),
                "Installation progress phases were not reported in order.");
            Assert(!Directory.EnumerateFileSystemEntries(layout.StagingPath).Any(), "Successful install left staging files behind.");
        }

        private static void ExtractsSevenZipAndRar(string root)
        {
            var sevenZipPath = WriteFixture(
                root,
                "sevenzip-t1.7z",
                "N3q8ryccAARTpfDIYgAAAAAAAAAgAAAAAAAAAMDMhcxiYXIKZm9vCgAAgTMHrjGYapZFTXUTjwzctMaE+1oPqd0uzZmXHJ6j4QB74vYCpg9q7Ktujb3oJ3hy4W538W7Jb5vgkQYVBSEqe1ACMsErIekjytgvhTh7gy6cjpHQfsAAABcGCAEJWgAHCwEAASMDAQEFXQAQAAAMZgoB3ZHz8QAA");
            var sevenZipRoot = Path.Combine(root, "sevenzip-content");
            var sevenZipResult = new SafeSharpCompressExtractor(SourcePackageKind.SevenZipArchive)
                .Extract(sevenZipPath, sevenZipRoot, CancellationToken.None);
            Assert(File.Exists(Path.Combine(sevenZipResult.PayloadRoot, "bar")), "7z file 'bar' was not extracted.");
            Assert(File.Exists(Path.Combine(sevenZipResult.PayloadRoot, "foo")), "7z file 'foo' was not extracted.");

            var rarPath = WriteFixture(
                root,
                "rar5-subdirs.rar",
                "UmFyIRoHAQDz4YLrCwEFBwAGAQGAgIAAWyrxsjACAwuGAASGAKSDAsekBMmAAAESc3ViL2RpcjIvZmlsZTIudHh0CgMTCNwVX4XkBhNmaWxlMgokNHkgOAIDC4gABIgApIMCfSS3cYAAARpzdWIvd2l0aCBzcGFjZS9sb25nIGZuLnR4dAoDEyncFV9mv8sdbG9uZyBmbgoOjzxzOAIDC4UABIUApIMCwYnsL4AAARpzdWIvw7zItcSpw7bhuIvDqC9maWxlLnR4dAoDE0TdFV+dEHMIZmlsZQqvrxG4MAIDC4YABIYApIMCBPcp4oAAARJzdWIvZGlyMS9maWxlMS50eHQKAxP92xVfHJEnNGZpbGUxCtVl6Z4kAgMLAAUA7YMBAAAAAIAAAQhzdWIvZGlyMgoDEwjcFV/ICfsT1nxQqSoCAwsABQDtgwEAAAAAgAABDnN1Yi93aXRoIHNwYWNlCgMTKdwVX1vbgh6UOOweJQIDCwAFAO2DAQAAAACAAAEJc3ViL2VtcHR5CgMT5dsVX/bv4ArIG6fPLQIDCwAFAO2DAQAAAACAAAERc3ViL8O8yLXEqcO24biLw6gKAxNE3RVfmSwqCYEdQEkkAgMLAAUA7YMBAAAAAIAAAQhzdWIvZGlyMQoDE/3bFV8Nrd40msCgER8CAwsABQDtgwEAAAAAgAABA3N1YgoDEzLdFV+8OWMOHXdWUQMFBAA=");
            var rarRoot = Path.Combine(root, "rar-content");
            var rarResult = new SafeSharpCompressExtractor(SourcePackageKind.RarArchive)
                .Extract(rarPath, rarRoot, CancellationToken.None);
            Assert(
                File.Exists(Path.Combine(rarResult.PayloadRoot, "dir1", "file1.txt")),
                "RAR nested file was not extracted.");
        }

        private static void RejectsRarLinks(string root)
        {
            var archivePath = WriteFixture(
                root,
                "rar5-symlink-unix.rar",
                "UmFyIRoHAQAzkrXlCgEFBgAFAQGAgABuR35XMgIDGAAECP/DAgAAAACAQAEJZGF0YV9saW5rCgMTBuQdXzlDegcMBQEACGRhdGEudHh0/Yr6ESYCAwuFAASFALSDAoLFweaAQAEIZGF0YS50eHQKAxPt4x1f8MPfIWRhdGEKXgUHFDgCAxwABAz/wwIAAAAAgEABC3JhbmRvbV9saW5rCgMTSOgdX2WpcDUQBQEADC4uL3JhbmRvbTEyMx13VlEDBQQA");
            try
            {
                new SafeSharpCompressExtractor(SourcePackageKind.RarArchive).Extract(
                    archivePath,
                    Path.Combine(root, "rar-link-content"),
                    CancellationToken.None);
                throw new InvalidOperationException("RAR links were accepted.");
            }
            catch (InvalidDataException)
            {
            }
        }

        private static void ExtractsWrapperAndSelectsGameExecutable(string root)
        {
            var archivePath = Path.Combine(root, "plasma.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "Plasma Pong/Plasma Pong.exe", "game");
                WriteEntry(archive, "Plasma Pong/unins000.exe", "uninstaller");
                WriteEntry(archive, "Plasma Pong/config.ini", "config");
            }

            var extraction = new SafeZipExtractor().Extract(archivePath, Path.Combine(root, "plasma-content"));
            Assert(Path.GetFileName(extraction.PayloadRoot) == "Plasma Pong", "Single wrapper directory was not collapsed.");
            var launch = new LaunchTargetResolver(new GameTitleNormalizer()).Resolve(extraction.PayloadRoot, "Plasma Pong");
            Assert(launch == "Plasma Pong.exe", "Main executable was not selected over the uninstaller.");
        }

        private static void RejectsTraversal(string root)
        {
            var archivePath = Path.Combine(root, "traversal.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "../escape.exe", "bad");
            }

            try
            {
                new SafeZipExtractor().Extract(archivePath, Path.Combine(root, "traversal-content"));
                throw new InvalidOperationException("Traversal archive was accepted.");
            }
            catch (InvalidDataException)
            {
            }
        }

        private static void DeletesOnlyManifestValidatedManagedInstallations(string root)
        {
            var managedRoot = Path.Combine(root, "managed");
            Assert(ManagedStorageLayout.TryCreate(managedRoot, out var layout, out var error), error);
            layout.EnsureCreated();
            Func<ManagedStorageLayout> layoutFactory = () => layout;
            var manifestStore = new InstallationManifestStore(layoutFactory);
            var installer = new ManagedArchiveInstaller(
                layoutFactory,
                new ProviderRegistry(Array.Empty<ICloudSourceProvider>()),
                new ArchiveExtractorRegistry(new IArchiveExtractor[] { new SafeZipExtractor() }),
                new LaunchTargetResolver(new GameTitleNormalizer()),
                manifestStore);

            const string gameId = "provider:account:object";
            var gameDirectory = Path.Combine(layout.GamesPath, "Test Game");
            Directory.CreateDirectory(gameDirectory);
            File.WriteAllText(Path.Combine(gameDirectory, "game.exe"), "game");
            manifestStore.Write(gameDirectory, new InstallManifest
            {
                GameId = gameId,
                GameName = "Test Game",
                LaunchTarget = "game.exe"
            });
            installer.Uninstall(gameId, gameDirectory);
            Assert(!Directory.Exists(gameDirectory), "Validated managed installation was not removed.");

            var outsideDirectory = Path.Combine(root, "outside");
            Directory.CreateDirectory(outsideDirectory);
            File.WriteAllText(Path.Combine(outsideDirectory, "game.exe"), "game");
            manifestStore.Write(outsideDirectory, new InstallManifest
            {
                GameId = gameId,
                GameName = "Outside",
                LaunchTarget = "game.exe"
            });
            try
            {
                installer.Uninstall(gameId, outsideDirectory);
                throw new InvalidOperationException("Outside installation was accepted for deletion.");
            }
            catch (InvalidDataException)
            {
            }

            Assert(Directory.Exists(outsideDirectory), "Directory outside the managed Games root was deleted.");
        }

        private static void ReconcilesOnlyAnAuthoritativeProviderAccountScope()
        {
            var pluginId = Guid.NewGuid();
            var otherPluginId = Guid.NewGuid();
            var unavailableTagId = Guid.NewGuid();
            var scope = new CloudSourceScope("google-drive", "account-a");
            var present = Game("google-drive:account-a:present", pluginId, false, unavailableTagId);
            var missingUninstalled = Game("google-drive:account-a:missing", pluginId, false);
            var missingInstalled = Game("google-drive:account-a:installed", pluginId, true);
            var missingManifest = Game("google-drive:account-a:manifest", pluginId, false);
            var otherAccount = Game("google-drive:account-b:other", pluginId, false);
            var otherPlugin = Game("google-drive:account-a:foreign", otherPluginId, false);
            var malformed = Game("not-a-cloud-id", pluginId, false);
            var games = new[]
            {
                present, missingUninstalled, missingInstalled, missingManifest,
                otherAccount, otherPlugin, malformed
            };

            var plan = new CloudLibraryReconciliationPlanner().CreatePlan(
                games,
                pluginId,
                scope,
                new[] { "google-drive:account-a:present" },
                game => ReferenceEquals(game, missingManifest),
                unavailableTagId);

            Assert(plan.GamesToMarkAvailable.SequenceEqual(new[] { present }), "Returned game was not marked available.");
            Assert(plan.GamesToRemove.SequenceEqual(new[] { missingUninstalled }), "Missing uninstalled game removal plan is incorrect.");
            Assert(
                new HashSet<global::Playnite.SDK.Models.Game>(plan.GamesToMarkUnavailable).SetEquals(new[] { missingInstalled, missingManifest }),
                "Installed missing games were not retained and marked unavailable.");
            Assert(!plan.GamesToRemove.Contains(otherAccount), "A different account was reconciled.");
            Assert(!plan.GamesToRemove.Contains(otherPlugin), "A different plugin was reconciled.");
            Assert(!plan.GamesToRemove.Contains(malformed), "A malformed identity was reconciled.");
        }

        private static global::Playnite.SDK.Models.Game Game(
            string gameId,
            Guid pluginId,
            bool installed,
            params Guid[] tagIds)
        {
            return new global::Playnite.SDK.Models.Game
            {
                GameId = gameId,
                PluginId = pluginId,
                IsInstalled = installed,
                TagIds = tagIds?.ToList()
            };
        }

        private static void WriteEntry(ZipArchive archive, string path, string contents)
        {
            var entry = archive.CreateEntry(path);
            using (var writer = new StreamWriter(entry.Open())) writer.Write(contents);
        }

        private static string WriteFixture(string root, string fileName, string base64)
        {
            var path = Path.Combine(root, fileName);
            File.WriteAllBytes(path, Convert.FromBase64String(base64));
            return path;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class MemoryProvider : ICloudSourceProvider
        {
            private readonly byte[] packageBytes;

            public string Id => "test-provider";
            public string Name => "Memory";
            public bool IsConfigured => true;

            public MemoryProvider(byte[] packageBytes)
            {
                this.packageBytes = packageBytes ?? throw new ArgumentNullException(nameof(packageBytes));
            }

            public System.Threading.Tasks.Task<IReadOnlyList<SourcePackage>> ScanAsync(
                SourceScanRequest request,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public System.Threading.Tasks.Task<Stream> OpenReadAsync(
                SourcePackage package,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return System.Threading.Tasks.Task.FromResult<Stream>(new MemoryStream(packageBytes, writable: false));
            }
        }
    }
}
