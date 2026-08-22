using System;
using System.IO;

namespace CloudSource.Playnite.Storage
{
    public sealed class ManagedStorageLayout
    {
        public string RootPath { get; }
        public string GamesPath { get; }
        public string StagingPath { get; }
        public string CachePath { get; }

        private ManagedStorageLayout(string rootPath)
        {
            RootPath = rootPath;
            GamesPath = Path.Combine(rootPath, "Games");
            StagingPath = Path.Combine(rootPath, "Staging");
            CachePath = Path.Combine(rootPath, "Cache");
        }

        public static bool TryCreate(string configuredPath, out ManagedStorageLayout layout, out string error)
        {
            layout = null;
            error = null;

            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                error = "Managed root is required.";
                return false;
            }

            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
                if (!Path.IsPathRooted(expanded))
                {
                    error = "Managed root must be an absolute path.";
                    return false;
                }

                var fullPath = Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var filesystemRoot = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(fullPath, filesystemRoot, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Managed root cannot be a filesystem root.";
                    return false;
                }

                layout = new ManagedStorageLayout(fullPath);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                error = $"Managed root is invalid: {exception.Message}";
                return false;
            }
        }

        public void EnsureCreated()
        {
            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(GamesPath);
            Directory.CreateDirectory(StagingPath);
            Directory.CreateDirectory(CachePath);
        }

        public bool Contains(string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            var rootWithSeparator = RootPath + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(candidatePath);
            return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
    }
}
