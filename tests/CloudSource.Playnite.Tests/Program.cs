using CloudSource.Playnite.GameImport;
using CloudSource.Playnite.Emulation;
using CloudSource.Playnite.Installation;
using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Providers.GoogleDrive;
using CloudSource.Playnite.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
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
                RequiresExplicitSelectionForAmbiguousExecutable(root);
                RejectsTraversal(root);
                ExtractsSevenZipAndRar(root);
                RegistersEverySupportedArchiveKind();
                ClassifiesEmulatedPlatformPackages();
                RejectsCrossProviderScanResults();
                InstallsRomWithoutExtraction(root);
                ReportsInstallationPhases(root);
                InstallsArchivedInnoPackage(root);
                InstallsStandaloneInnoBundle(root);
                CompletesLegacyExtractedInstaller(root);
                RecoversCompletedNativeInstallWithoutRerunningSetup(root);
                CreatesAnEditablePlayniteAction(root);
                ExposesHttpContentLength();
                RejectsRarLinks(root);
                DeletesOnlyManifestValidatedManagedInstallations(root);
                PreservesNativeInstallerLeftovers(root);
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
                if (kind == SourcePackageKind.InnoInstallerBundle || kind == SourcePackageKind.RomFile) continue;
                Assert(registry.Supports(kind), $"Archive kind '{kind}' has no registered installer.");
            }

            Assert(!registry.Supports(SourcePackageKind.InnoInstallerBundle), "Native installer bundles must not use an archive extractor.");
            Assert(!registry.Supports(SourcePackageKind.RomFile), "ROM files must not use an archive extractor.");
        }

        private static void ClassifiesEmulatedPlatformPackages()
        {
            var classifier = new CloudArchiveClassifier();
            var arcade = classifier.Classify("My Drive/Games/Platforms/MAME/pacman.zip");
            Assert(arcade.ContentKind == CloudContentKind.Rom, "MAME ZIP was not classified as ROM content.");
            Assert(arcade.PlatformName == "Arcade", "MAME folder did not resolve to Playnite's Arcade platform.");
            Assert(arcade.PlatformSpecificationId == "arcade", "Arcade platform specification ID is incorrect.");
            var directMame = classifier.Classify("My Drive/Games/MAME/galaga.zip");
            Assert(directMame.ContentKind == CloudContentKind.Rom && directMame.PlatformSpecificationId == "arcade", "Direct MGA-style MAME path was not classified as Arcade ROM content.");
            var inferredGba = classifier.Classify("My Drive/Games/Metroid Fusion.gba");
            Assert(inferredGba.ContentKind == CloudContentKind.Rom && inferredGba.PlatformSpecificationId == "nintendo_gameboyadvance", "GBA platform was not inferred from its ROM extension.");

            var windows = classifier.Classify("My Drive/Games/Platforms/Windows/Hangman.zip");
            Assert(windows.ContentKind == CloudContentKind.NativePackage, "Windows package was incorrectly classified as ROM content.");
            Assert(windows.PlatformSpecificationId == "pc_windows", "Windows platform specification ID is incorrect.");

            var atari = classifier.Classify("My Drive/Games/Platforms/Atari 2600/game.a26");
            Assert(atari.ContentKind == CloudContentKind.Rom, "An ordinary path-classified ROM was not recognized.");
            Assert(atari.PlatformName == "Atari 2600", "Unknown platform folder name was not preserved for Playnite lookup.");
        }

        private static void RejectsCrossProviderScanResults()
        {
            var package = new SourcePackage(
                "test-provider",
                "account-a",
                "object",
                "revision",
                "Games/game.zip",
                "game.zip",
                1,
                null,
                SourcePackageKind.ZipArchive);
            var valid = new CloudProviderScanResult("test-provider", "account-a", new[] { package });
            Assert(valid.Packages.Single() == package, "Valid provider scan result lost its package.");

            AssertThrows<ArgumentException>(
                () => new CloudProviderScanResult("other-provider", "account-a", new[] { package }),
                "A provider was allowed to return another provider's package.");
            AssertThrows<ArgumentException>(
                () => new CloudProviderScanResult("test-provider", "account-b", new[] { package }),
                "A provider was allowed to return another account's package.");
        }

        private static void InstallsRomWithoutExtraction(string root)
        {
            var romBytes = new byte[] { 0x50, 0x4b, 0x03, 0x04, 1, 2, 3, 4 };
            var package = new SourcePackage(
                "test-provider",
                "account",
                "arcade-object",
                "revision",
                "Games/Platforms/Arcade/pacman.zip",
                "pacman.zip",
                romBytes.Length,
                null,
                SourcePackageKind.ZipArchive);
            var managedRoot = Path.Combine(root, "rom-managed");
            Assert(ManagedStorageLayout.TryCreate(managedRoot, out var layout, out var error), error);
            var manifestStore = new InstallationManifestStore(() => layout);
            var installer = new ManagedRomInstaller(
                () => layout,
                new ProviderRegistry(new ICloudSourceProvider[] { new MemoryProvider(romBytes) }),
                manifestStore);
            var emulatorId = Guid.NewGuid();
            var plan = new EmulatorInstallPlan(
                "Arcade",
                "arcade",
                emulatorId,
                "MAME",
                "mame-default",
                "Default",
                "-rompath \"{ImageDir}\"");
            var updates = new List<InstallationProgressUpdate>();

            var record = installer.Install(package, "Pac-Man", plan, updates.Add, CancellationToken.None);

            var installedRom = Path.Combine(record.InstallDirectory, "pacman.zip");
            Assert(File.ReadAllBytes(installedRom).SequenceEqual(romBytes), "ROM archive was changed or extracted during installation.");
            Assert(record.Manifest.InstallKind == "managed_rom", "ROM installation manifest has the wrong kind.");
            Assert(record.Manifest.RomTarget == "pacman.zip", "ROM target was not recorded.");
            Assert(manifestStore.Find(package.StableId) != null, "ROM installation manifest could not be reopened.");
            Assert(!Directory.EnumerateFileSystemEntries(layout.StagingPath).Any(), "ROM installation left staging files behind.");
            Assert(updates.All(update => update.Stage != InstallationProgressStage.Extracting), "ROM installation reported an extraction phase.");

            var game = new Game { Name = "Pac-Man" };
            var manager = new CloudGameActionManager();
            Assert(manager.EnsureEditableEmulatorAction(game, record, plan), "ROM emulator action was not created.");
            Assert(game.Roms.Single().Path == Path.Combine(ExpandableVariables.InstallationDirectory, "pacman.zip"), "Playnite ROM path is incorrect.");
            var action = game.GameActions.Single();
            Assert(action.Type == GameActionType.Emulator, "ROM action does not use Playnite's emulator action type.");
            Assert(action.EmulatorId == emulatorId && action.EmulatorProfileId == "mame-default", "ROM action does not target the selected emulator profile.");
            Assert(action.AdditionalArguments == "-rompath \"{ImageDir}\"", "MAME ROM directory argument is missing.");
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

            var record = installer.Install(package, "Progress Game", _ => true, _ => null, updates.Add, CancellationToken.None);

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
            var extractionUpdates = updates.Where(update => update.Stage == InstallationProgressStage.Extracting).ToList();
            Assert(extractionUpdates.Count > 0, "Extraction did not report determinate progress.");
            Assert(
                extractionUpdates.Last().CompletedBytes == extractionUpdates.Last().TotalBytes,
                "Extraction progress did not reach its expanded byte total.");
        }

        private static void ExposesHttpContentLength()
        {
            using (var response = new System.Net.Http.HttpResponseMessage())
            {
                response.Content = new System.Net.Http.ByteArrayContent(new byte[123]);
                using (var stream = new HttpResponseStream(new MemoryStream(new byte[123]), response))
                {
                    Assert(stream.ContentLength == 123, "Google Drive response content length was not exposed.");
                }
            }
        }

        private static void InstallsArchivedInnoPackage(string root)
        {
            var archivePath = Path.Combine(root, "archived-installer.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteBinaryEntry(archive, "package/setup.exe", InnoFixtureBytes());
                WriteBinaryEntry(archive, "package/fg-01.bin", new byte[] { 1, 2, 3 });
                WriteBinaryEntry(archive, "package/QuickSFV.EXE", new byte[] { 4, 5, 6 });
            }

            var packageBytes = File.ReadAllBytes(archivePath);
            var package = new SourcePackage(
                "test-provider", "account", "archive-object", "revision",
                "Games/Archive Game.zip", "Archive Game.zip", packageBytes.Length,
                null, SourcePackageKind.ZipArchive);
            var runner = new FakeNativeInstallerRunner("Archive Game.exe");
            var installer = CreateInstaller(
                Path.Combine(root, "archived-inno-managed"),
                new MemoryProvider(packageBytes),
                runner);
            var confirmations = 0;

            var record = installer.Install(
                package,
                "Archive Game",
                _ => { confirmations++; return true; },
                _ => null,
                _ => { },
                CancellationToken.None);

            Assert(confirmations == 1, "Archived unsigned installer did not require confirmation.");
            Assert(record.Manifest.InstallKind == "inno", "Archived installer was recorded as a portable archive.");
            Assert(record.Manifest.LaunchTarget == "Archive Game.exe", "Installed game executable was not resolved.");
            Assert(record.Manifest.UninstallTarget == "unins000.exe", "Installed Inno uninstaller was not recorded.");
            Assert(!File.Exists(Path.Combine(record.InstallDirectory, "setup.exe")), "Installer package was copied into the game directory.");
        }

        private static void InstallsStandaloneInnoBundle(string root)
        {
            var setup = InnoFixtureBytes();
            var companion = new byte[] { 7, 8, 9, 10 };
            var files = new[]
            {
                new SourcePackageFile(
                    "setup-object", "setup-revision", "Installers/setup_standalone_game_1.0_(1).exe",
                    "setup_standalone_game_1.0_(1).exe", setup.Length, SourcePackageFileRole.Primary),
                new SourcePackageFile(
                    "bin-object", "bin-revision", "Installers/setup_standalone_game_1.0_(1)-1.bin",
                    "setup_standalone_game_1.0_(1)-1.bin", companion.Length, SourcePackageFileRole.Companion)
            };
            var package = new SourcePackage(
                "test-provider", "account", "setup-object", "bundle-revision",
                files[0].LogicalPath, files[0].DisplayName, setup.Length + companion.Length,
                null, SourcePackageKind.InnoInstallerBundle, files);
            var provider = new PackageFileProvider(new Dictionary<string, byte[]>
            {
                ["setup-object"] = setup,
                ["bin-object"] = companion
            });
            var runner = new FakeNativeInstallerRunner("Standalone Game.exe")
            {
                RequiredCompanion = files[1].DisplayName
            };
            var installer = CreateInstaller(
                Path.Combine(root, "standalone-inno-managed"),
                provider,
                runner);

            var record = installer.Install(
                package,
                "Standalone Game",
                _ => true,
                _ => null,
                _ => { },
                CancellationToken.None);

            Assert(record.Manifest.InstallKind == "inno", "Standalone installer bundle was not recorded as Inno.");
            Assert(record.Manifest.ArchiveSizeBytes == setup.Length + companion.Length, "Standalone package byte total is incorrect.");
            Assert(runner.InstallCalls == 1, "Standalone setup process was not invoked exactly once.");
        }

        private static void CompletesLegacyExtractedInstaller(string root)
        {
            var managedRoot = Path.Combine(root, "legacy-installer-managed");
            Assert(ManagedStorageLayout.TryCreate(managedRoot, out var layout, out var error), error);
            layout.EnsureCreated();
            var legacyDirectory = Path.Combine(layout.GamesPath, "Legacy Game");
            Directory.CreateDirectory(legacyDirectory);
            File.WriteAllBytes(Path.Combine(legacyDirectory, "setup.exe"), InnoFixtureBytes());
            File.WriteAllText(Path.Combine(legacyDirectory, "QuickSFV.EXE"), "utility");
            var manifestStore = new InstallationManifestStore(() => layout);
            manifestStore.Write(legacyDirectory, new InstallManifest
            {
                SchemaVersion = 1,
                GameId = "test-provider:account:legacy-object",
                GameName = "Legacy Game",
                ProviderId = "test-provider",
                AccountId = "account",
                ObjectId = "legacy-object",
                Revision = "revision",
                LogicalPath = "Games/Legacy Game.7z",
                ArchiveSha256 = "hash",
                ArchiveSizeBytes = 100,
                InstalledSizeBytes = 50,
                LaunchTarget = "QuickSFV.EXE"
            });
            var runner = new FakeNativeInstallerRunner("Legacy Game.exe");
            var resolver = new LaunchTargetResolver(new GameTitleNormalizer());
            var installer = new ManagedArchiveInstaller(
                () => layout,
                new ProviderRegistry(Array.Empty<ICloudSourceProvider>()),
                new ArchiveExtractorRegistry(Array.Empty<IArchiveExtractor>()),
                resolver,
                manifestStore,
                new InstallerPackageClassifier(),
                new NativeInnoInstaller(resolver, runner));

            Assert(installer.CanCompleteExtractedInstaller("test-provider:account:legacy-object", legacyDirectory),
                "Legacy extracted installer was not offered for completion.");
            var record = installer.CompleteExtractedInstaller(
                "test-provider:account:legacy-object",
                legacyDirectory,
                "Legacy Game",
                _ => true,
                _ => null,
                _ => { },
                CancellationToken.None);

            Assert(!Directory.Exists(legacyDirectory), "Legacy extracted installer package was retained after successful native install.");
            Assert(record.Manifest.SchemaVersion == 2 && record.Manifest.InstallKind == "inno", "Legacy installer manifest was not upgraded.");
            Assert(File.Exists(Path.Combine(record.InstallDirectory, "Legacy Game.exe")), "Recovered native game executable is missing.");
        }

        private static ManagedArchiveInstaller CreateInstaller(
            string managedRoot,
            ICloudSourceProvider provider,
            INativeInstallerProcessRunner runner)
        {
            Assert(ManagedStorageLayout.TryCreate(managedRoot, out var layout, out var error), error);
            var manifestStore = new InstallationManifestStore(() => layout);
            var resolver = new LaunchTargetResolver(new GameTitleNormalizer());
            return new ManagedArchiveInstaller(
                () => layout,
                new ProviderRegistry(new[] { provider }),
                new ArchiveExtractorRegistry(new IArchiveExtractor[]
                {
                    new SafeZipExtractor(),
                    new SafeSharpCompressExtractor(SourcePackageKind.SevenZipArchive),
                    new SafeSharpCompressExtractor(SourcePackageKind.RarArchive)
                }),
                resolver,
                manifestStore,
                new InstallerPackageClassifier(),
                new NativeInnoInstaller(resolver, runner));
        }

        private static byte[] InnoFixtureBytes()
        {
            return System.Text.Encoding.ASCII.GetBytes("MZ test fixture Inno Setup installer");
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

        private static void RequiresExplicitSelectionForAmbiguousExecutable(string root)
        {
            var gameRoot = Path.Combine(root, "ambiguous-launch-target");
            Directory.CreateDirectory(gameRoot);
            File.WriteAllText(Path.Combine(gameRoot, "BUILD.EXE"), "editor");
            File.WriteAllText(Path.Combine(gameRoot, "DUKE3D.EXE"), "game");
            var resolver = new LaunchTargetResolver(new GameTitleNormalizer());
            LaunchTargetSelectionRequest request = null;

            var selected = resolver.Resolve(
                gameRoot,
                "Duke Nukem 3D",
                value =>
                {
                    request = value;
                    return "DUKE3D.EXE";
                });

            Assert(selected == "DUKE3D.EXE", "Explicitly selected executable was not returned.");
            Assert(request != null && request.Candidates.Count == 2, "Executable picker did not receive every safe candidate.");

            try
            {
                resolver.Resolve(gameRoot, "Duke Nukem 3D", _ => "outside.exe");
                throw new InvalidOperationException("An executable outside the validated candidate set was accepted.");
            }
            catch (InvalidDataException)
            {
            }

            try
            {
                resolver.Resolve(gameRoot, "Duke Nukem 3D", _ => null);
                throw new InvalidOperationException("Canceling executable selection did not cancel finalization.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static void RecoversCompletedNativeInstallWithoutRerunningSetup(string root)
        {
            var managedRoot = Path.Combine(root, "native-recovery-managed");
            Assert(ManagedStorageLayout.TryCreate(managedRoot, out var layout, out var error), error);
            layout.EnsureCreated();
            var destination = Path.Combine(layout.GamesPath, "Duke Nukem 3D");
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "BUILD.EXE"), "editor");
            File.WriteAllText(Path.Combine(destination, "DUKE3D.EXE"), "game");
            File.WriteAllText(Path.Combine(destination, "Launch Duke Nukem 3D.lnk"), "shortcut");
            File.WriteAllText(Path.Combine(destination, "unins000.exe"), "uninstaller");

            var provider = new MemoryProvider(new byte[] { 1, 2, 3 });
            var runner = new FakeNativeInstallerRunner("unused.exe");
            var installer = CreateInstaller(managedRoot, provider, runner);
            var package = new SourcePackage(
                "test-provider",
                "account",
                "duke-object",
                "revision",
                "Installers/setup_duke_nukem_3d.exe",
                "setup_duke_nukem_3d.exe",
                3,
                null,
                SourcePackageKind.InnoInstallerBundle);
            var selections = 0;

            var record = installer.Install(
                package,
                "Duke Nukem 3D",
                _ => throw new InvalidOperationException("Recovery must not ask to rerun setup."),
                request =>
                {
                    selections++;
                    Assert(request.InstallDirectory == destination, "Recovery picker showed the wrong managed directory.");
                    Assert(request.Candidates.Any(candidate => candidate.RelativePath == "Launch Duke Nukem 3D.lnk"),
                        "Recovery picker omitted the installed game shortcut.");
                    return "Launch Duke Nukem 3D.lnk";
                },
                _ => { },
                CancellationToken.None);

            Assert(selections == 1, "Recovery did not request an explicit executable selection.");
            Assert(provider.OpenCalls == 0, "Recovery downloaded the installer package again.");
            Assert(runner.InstallCalls == 0, "Recovery reran the native installer.");
            Assert(record.InstallDirectory == destination, "Recovery changed the completed installation directory.");
            Assert(record.Manifest.LaunchTarget == "Launch Duke Nukem 3D.lnk", "Recovery persisted the wrong launcher.");
            Assert(record.Manifest.InvocationMode == "recovered_existing_post_install", "Recovery origin was not recorded.");
            Assert(File.Exists(Path.Combine(destination, InstallationManifestStore.FileName)), "Recovery did not write the managed manifest.");
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

        private static void CreatesAnEditablePlayniteAction(string root)
        {
            var installDirectory = Path.Combine(root, "editable-action");
            Directory.CreateDirectory(Path.Combine(installDirectory, "bin"));
            var manifest = new InstallManifest
            {
                GameId = "editable-action",
                GameName = "Editable Action",
                LaunchTarget = Path.Combine("bin", "game.exe")
            };
            var record = new InstallationRecord(installDirectory, manifest);
            var game = new Game();

            var manager = new CloudGameActionManager();
            Assert(manager.EnsureEditablePlayAction(game, record), "The native Playnite action was not created.");
            var action = game.GameActions.Single();
            Assert(action.Name == CloudGameActionManager.ManagedActionName, "The managed action name is incorrect.");
            Assert(action.IsPlayAction && action.Type == GameActionType.File, "The managed action is not a file play action.");
            Assert(
                action.Path == Path.Combine(ExpandableVariables.InstallationDirectory, "bin", "game.exe"),
                "The managed action does not use Playnite's install-directory variable.");
            Assert(
                action.WorkingDir == Path.Combine(ExpandableVariables.InstallationDirectory, "bin"),
                "The managed action working directory is incorrect.");
            Assert(!manager.EnsureEditablePlayAction(game, record), "The existing managed action was duplicated.");
            Assert(game.GameActions.Count == 1, "Repeated action synchronization created a duplicate.");
            action.Path = @"C:\Custom\launcher.exe";
            Assert(!manager.EnsureEditablePlayAction(game, record), "A user-edited managed action was replaced.");
            Assert(action.Path == @"C:\Custom\launcher.exe", "The user's launcher edit was overwritten.");
        }

        private static void PreservesNativeInstallerLeftovers(string root)
        {
            var managedRoot = Path.Combine(root, "native-uninstall-managed");
            Assert(ManagedStorageLayout.TryCreate(managedRoot, out var layout, out var error), error);
            layout.EnsureCreated();
            var gameDirectory = Path.Combine(layout.GamesPath, "Save Game");
            Directory.CreateDirectory(gameDirectory);
            File.WriteAllText(Path.Combine(gameDirectory, "game.exe"), "game");
            File.WriteAllText(Path.Combine(gameDirectory, "unins000.exe"), "uninstaller");
            File.WriteAllText(Path.Combine(gameDirectory, "save.dat"), "do not delete");
            var manifestStore = new InstallationManifestStore(() => layout);
            manifestStore.Write(gameDirectory, new InstallManifest
            {
                GameId = "native-save-game",
                GameName = "Save Game",
                LaunchTarget = "game.exe",
                InstallKind = "inno",
                UninstallTarget = "unins000.exe"
            });
            var runner = new FakeNativeInstallerRunner("unused.exe");
            var installer = CreateInstaller(
                managedRoot,
                new MemoryProvider(new byte[] { 1 }),
                runner);

            var result = installer.Uninstall("native-save-game", gameDirectory);

            Assert(result.RemainingFilesPreserved, "Native uninstaller leftovers were not reported as preserved.");
            Assert(File.Exists(Path.Combine(gameDirectory, "save.dat")), "A save left by the native uninstaller was deleted.");
            Assert(!File.Exists(Path.Combine(gameDirectory, InstallationManifestStore.FileName)), "The installation manifest was retained after uninstall.");
            Assert(
                runner.LastUninstallArguments == "/NORESTART",
                "The native uninstaller was not run visibly for user-controlled save retention.");
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

        private static void WriteBinaryEntry(ZipArchive archive, string path, byte[] contents)
        {
            var entry = archive.CreateEntry(path);
            using (var output = entry.Open()) output.Write(contents, 0, contents.Length);
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

        private static void AssertThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }

        private abstract class TestCloudProvider : ICloudSourceProvider
        {
            public string Id => "test-provider";
            public abstract string Name { get; }
            public bool IsConfigured => true;
            public bool HasStoredConnection => true;
            public bool HasPendingConnection => false;

            public System.Threading.Tasks.Task<CloudProviderAccount> ConnectAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public void CommitPendingConnection() => throw new NotSupportedException();
            public void DiscardPendingConnection() { }
            public void Disconnect() => throw new NotSupportedException();
            public CloudProviderFolder SelectSourceFolder(string existingSelectionPath) => throw new NotSupportedException();
            public System.Threading.Tasks.Task<IReadOnlyList<CloudProviderScanResult>> ScanAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public abstract System.Threading.Tasks.Task<Stream> OpenReadAsync(
                SourcePackage package,
                CancellationToken cancellationToken);
            public abstract System.Threading.Tasks.Task<Stream> OpenReadFileAsync(
                SourcePackage package,
                SourcePackageFile file,
                CancellationToken cancellationToken);
        }

        private sealed class MemoryProvider : TestCloudProvider
        {
            private readonly byte[] packageBytes;

            public int OpenCalls { get; private set; }

            public override string Name => "Memory";

            public MemoryProvider(byte[] packageBytes)
            {
                this.packageBytes = packageBytes ?? throw new ArgumentNullException(nameof(packageBytes));
            }

            public override System.Threading.Tasks.Task<Stream> OpenReadAsync(
                SourcePackage package,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OpenCalls++;
                return System.Threading.Tasks.Task.FromResult<Stream>(new MemoryStream(packageBytes, writable: false));
            }

            public override System.Threading.Tasks.Task<Stream> OpenReadFileAsync(
                SourcePackage package,
                SourcePackageFile file,
                CancellationToken cancellationToken)
            {
                return OpenReadAsync(package, cancellationToken);
            }
        }

        private sealed class PackageFileProvider : TestCloudProvider
        {
            private readonly IReadOnlyDictionary<string, byte[]> files;

            public override string Name => "Package files";

            public PackageFileProvider(IReadOnlyDictionary<string, byte[]> files)
            {
                this.files = files ?? throw new ArgumentNullException(nameof(files));
            }

            public override System.Threading.Tasks.Task<Stream> OpenReadAsync(
                SourcePackage package,
                CancellationToken cancellationToken)
            {
                return OpenReadFileAsync(
                    package,
                    package.Files.Single(file => file.Role == SourcePackageFileRole.Primary),
                    cancellationToken);
            }

            public override System.Threading.Tasks.Task<Stream> OpenReadFileAsync(
                SourcePackage package,
                SourcePackageFile file,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!files.TryGetValue(file.ObjectId, out var contents)) throw new FileNotFoundException(file.ObjectId);
                return System.Threading.Tasks.Task.FromResult<Stream>(new MemoryStream(contents, writable: false));
            }
        }

        private sealed class FakeNativeInstallerRunner : INativeInstallerProcessRunner
        {
            private readonly string gameExecutable;

            public int InstallCalls { get; private set; }
            public string RequiredCompanion { get; set; }
            public string LastUninstallArguments { get; private set; }

            public FakeNativeInstallerRunner(string gameExecutable)
            {
                this.gameExecutable = gameExecutable;
            }

            public int Run(string path, string arguments, string workingDirectory)
            {
                if (Path.GetFileName(path).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                {
                    LastUninstallArguments = arguments;
                    return 0;
                }
                InstallCalls++;
                if (!string.IsNullOrWhiteSpace(RequiredCompanion) && !File.Exists(Path.Combine(workingDirectory, RequiredCompanion)))
                    throw new InvalidOperationException("Installer companion was not staged beside setup.");
                const string prefix = "/DIR=\"";
                var start = arguments.IndexOf(prefix, StringComparison.Ordinal);
                if (start < 0) throw new InvalidOperationException("Managed destination argument is missing.");
                start += prefix.Length;
                var end = arguments.IndexOf('"', start);
                if (end < 0) throw new InvalidOperationException("Managed destination argument is invalid.");
                var destination = arguments.Substring(start, end - start);
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, gameExecutable), "game");
                File.WriteAllText(Path.Combine(destination, "unins000.exe"), "uninstaller");
                return 0;
            }
        }
    }
}
