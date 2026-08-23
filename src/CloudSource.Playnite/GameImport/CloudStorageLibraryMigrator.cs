using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudSource.Playnite.GameImport
{
    internal sealed class CloudStorageLibraryMigrator
    {
        private const string ArchiveDescriptionPrefix = "Cloud archive: ";
        private readonly IGameDatabaseAPI database;
        private readonly GameTitleNormalizer titleNormalizer;
        private readonly CloudArchiveClassifier archiveClassifier;

        public CloudStorageLibraryMigrator(
            IGameDatabaseAPI database,
            GameTitleNormalizer titleNormalizer,
            CloudArchiveClassifier archiveClassifier)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.titleNormalizer = titleNormalizer ?? throw new ArgumentNullException(nameof(titleNormalizer));
            this.archiveClassifier = archiveClassifier ?? throw new ArgumentNullException(nameof(archiveClassifier));
        }

        public CloudStorageMigrationResult Migrate()
        {
            var games = database.Games
                .Where(game => game.PluginId == CloudSourcePlugin.PluginId)
                .ToList();
            if (games.Count == 0)
            {
                return new CloudStorageMigrationResult();
            }

            var result = new CloudStorageMigrationResult();
            using (database.BufferedUpdate())
            {
                result.RenamedSources = MigrateSourceName(games);
                foreach (var game in games)
                {
                    if (!TryGetLogicalPath(game.Description, out var logicalPath))
                    {
                        continue;
                    }

                    var updated = false;
                    var rawTitle = archiveClassifier.GetRawTitleFromLogicalPath(logicalPath);
                    if (string.Equals(game.Name, rawTitle, StringComparison.Ordinal))
                    {
                        var cleanTitle = titleNormalizer.CleanDisplayTitle(rawTitle);
                        if (!string.Equals(cleanTitle, game.Name, StringComparison.Ordinal))
                        {
                            game.Name = cleanTitle;
                            result.CleanedTitles++;
                            updated = true;
                        }
                    }

                    if ((game.PlatformIds?.Count ?? 0) > 0)
                    {
                        if (updated)
                        {
                            database.Games.Update(game);
                        }

                        continue;
                    }

                    var platformName = archiveClassifier.ResolvePlatform(logicalPath);
                    var platform = database.Platforms.FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, platformName, StringComparison.OrdinalIgnoreCase));
                    if (platform != null)
                    {
                        game.PlatformIds = new List<Guid> { platform.Id };
                        result.AssignedPlatforms++;
                        updated = true;
                    }

                    if (updated)
                    {
                        database.Games.Update(game);
                    }
                }
            }

            return result;
        }

        private int MigrateSourceName(IReadOnlyCollection<Game> games)
        {
            var sourceIds = new HashSet<Guid>(games.Select(game => game.SourceId));
            var legacySources = database.Sources
                .Where(source => sourceIds.Contains(source.Id) &&
                    string.Equals(source.Name, CloudStorageProduct.LegacyDisplayName, StringComparison.Ordinal))
                .ToList();
            if (legacySources.Count == 0)
            {
                return 0;
            }

            var currentSource = database.Sources.FirstOrDefault(source =>
                string.Equals(source.Name, CloudStorageProduct.DisplayName, StringComparison.Ordinal));
            if (currentSource == null)
            {
                foreach (var source in legacySources)
                {
                    source.Name = CloudStorageProduct.DisplayName;
                    database.Sources.Update(source);
                }

                return legacySources.Count;
            }

            var legacyIds = new HashSet<Guid>(legacySources.Select(source => source.Id));
            foreach (var game in games.Where(game => legacyIds.Contains(game.SourceId)))
            {
                game.SourceId = currentSource.Id;
                database.Games.Update(game);
            }

            return legacySources.Count;
        }

        private static bool TryGetLogicalPath(string description, out string logicalPath)
        {
            logicalPath = null;
            if (string.IsNullOrWhiteSpace(description) ||
                !description.StartsWith(ArchiveDescriptionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            logicalPath = description.Substring(ArchiveDescriptionPrefix.Length).Trim();
            return logicalPath.Length > 0;
        }
    }

    internal sealed class CloudStorageMigrationResult
    {
        public int RenamedSources { get; set; }
        public int CleanedTitles { get; set; }
        public int AssignedPlatforms { get; set; }
        public bool Changed => RenamedSources > 0 || CleanedTitles > 0 || AssignedPlatforms > 0;
    }
}
