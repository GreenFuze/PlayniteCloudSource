using CloudSource.Playnite.GameImport;
using CloudSource.Playnite.Installation;
using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

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

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
