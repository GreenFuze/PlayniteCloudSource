using CloudSource.Playnite.Providers;
using Playnite.SDK.Models;
using System;
using System.IO;

namespace CloudSource.Playnite.Installation
{
    internal sealed class CloudPackageResolver
    {
        private const string DescriptionPrefix = "Cloud archive: ";

        public SourcePackage Resolve(Game game)
        {
            if (game == null)
            {
                throw new ArgumentNullException(nameof(game));
            }

            var identity = (game.GameId ?? string.Empty).Split(new[] { ':' }, 3);
            if (identity.Length != 3 || identity[0].Length == 0 || identity[1].Length == 0 || identity[2].Length == 0)
            {
                throw new InvalidDataException($"Game '{game.Name}' has an invalid Cloud Storage identity.");
            }

            if (string.IsNullOrWhiteSpace(game.Description) ||
                !game.Description.StartsWith(DescriptionPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Game '{game.Name}' has no cloud archive path.");
            }

            var logicalPath = game.Description.Substring(DescriptionPrefix.Length).Trim();
            var displayName = Path.GetFileName(logicalPath);
            return new SourcePackage(
                identity[0], identity[1], identity[2],
                string.IsNullOrWhiteSpace(game.Version) ? "unknown" : game.Version,
                logicalPath, displayName, 0, null, ResolveKind(displayName));
        }

        private static SourcePackageKind ResolveKind(string fileName)
        {
            switch (Path.GetExtension(fileName)?.ToLowerInvariant())
            {
                case ".zip": return SourcePackageKind.ZipArchive;
                case ".7z": return SourcePackageKind.SevenZipArchive;
                case ".rar": return SourcePackageKind.RarArchive;
                default: throw new NotSupportedException($"Archive type for '{fileName}' is not supported.");
            }
        }
    }
}
