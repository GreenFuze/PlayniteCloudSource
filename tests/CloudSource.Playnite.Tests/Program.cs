using CloudSource.Playnite.GameImport;
using CloudSource.Playnite.Installation;
using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Storage;
using System;
using System.IO;
using System.IO.Compression;

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
                DeletesOnlyManifestValidatedManagedInstallations(root);
                Console.WriteLine("All Cloud Storage installer tests passed.");
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
            var installer = new ManagedZipInstaller(
                layoutFactory,
                new ProviderRegistry(Array.Empty<ICloudSourceProvider>()),
                new SafeZipExtractor(),
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

        private static void WriteEntry(ZipArchive archive, string path, string contents)
        {
            var entry = archive.CreateEntry(path);
            using (var writer = new StreamWriter(entry.Open())) writer.Write(contents);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
