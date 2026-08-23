using CloudSource.Playnite.Storage;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CloudSource.Playnite.Installation
{
    internal sealed class InstallationManifestStore
    {
        public const string FileName = ".cloud-storage-install.json";
        private readonly Func<ManagedStorageLayout> layoutFactory;

        public InstallationManifestStore(Func<ManagedStorageLayout> layoutFactory)
        {
            this.layoutFactory = layoutFactory ?? throw new ArgumentNullException(nameof(layoutFactory));
        }

        public void Write(string installDirectory, InstallManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var path = Path.Combine(installDirectory, FileName);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(InstallManifest)).WriteObject(stream, manifest);
            }
        }

        public InstallationRecord Find(string gameId, string preferredDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return null;
            }

            var layout = layoutFactory();
            if (TryRead(preferredDirectory, gameId, out var preferred))
            {
                return preferred;
            }

            if (!Directory.Exists(layout.GamesPath))
            {
                return null;
            }

            return Directory.EnumerateDirectories(layout.GamesPath)
                .Select(directory => TryRead(directory, gameId, out var record) ? record : null)
                .FirstOrDefault(record => record != null);
        }

        public bool IsManagedGameDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            var gamesRoot = Path.GetFullPath(layoutFactory().GamesPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(gamesRoot, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate, gamesRoot, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryRead(string directory, string expectedGameId, out InstallationRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || !IsManagedGameDirectory(directory))
            {
                return false;
            }

            var path = Path.Combine(directory, FileName);
            if (!TryReadManifest(path, out var manifest) || manifest == null ||
                manifest.SchemaVersion != 1 ||
                !string.Equals(manifest.GameId, expectedGameId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.LaunchTarget))
            {
                return false;
            }

            var launchPath = Path.GetFullPath(Path.Combine(directory, manifest.LaunchTarget));
            var directoryPrefix = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!launchPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(launchPath))
            {
                return false;
            }

            record = new InstallationRecord(directory, manifest);
            return true;
        }

        private static bool TryReadManifest(string path, out InstallManifest manifest)
        {
            manifest = null;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    manifest = (InstallManifest)new DataContractJsonSerializer(typeof(InstallManifest)).ReadObject(stream);
                    return manifest != null;
                }
            }
            catch (Exception exception) when (
                exception is SerializationException ||
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
