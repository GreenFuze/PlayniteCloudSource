using CloudSource.Playnite.Installation;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudSource.Playnite.GameImport
{
    internal sealed class CloudLibraryReconciler
    {
        public static readonly Guid UnavailableTagId = Guid.Parse("a8c090d7-e63b-4a82-94c9-622f6ce6dad3");
        public const string UnavailableTagName = "Cloud Storage: Source unavailable";

        private readonly IGameDatabaseAPI database;
        private readonly InstallationManifestStore manifestStore;
        private readonly CloudLibraryReconciliationPlanner planner;

        public CloudLibraryReconciler(
            IGameDatabaseAPI database,
            InstallationManifestStore manifestStore,
            CloudLibraryReconciliationPlanner planner)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        }

        public CloudLibraryReconciliationResult Reconcile(AuthoritativeSourceSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var plan = planner.CreatePlan(
                database.Games.ToList(),
                CloudSourcePlugin.PluginId,
                snapshot.Scope,
                snapshot.GameIds,
                game => manifestStore.Find(game.GameId, game.InstallDirectory) != null,
                UnavailableTagId);

            using (database.BufferedUpdate())
            {
                if (plan.GamesToMarkUnavailable.Count > 0)
                {
                    EnsureUnavailableTagExists();
                    foreach (var game in plan.GamesToMarkUnavailable)
                    {
                        var tagIds = game.TagIds?.ToList() ?? new List<Guid>();
                        tagIds.Add(UnavailableTagId);
                        game.TagIds = tagIds.Distinct().ToList();
                        database.Games.Update(game);
                    }
                }

                foreach (var game in plan.GamesToMarkAvailable)
                {
                    game.TagIds = game.TagIds?
                        .Where(tagId => tagId != UnavailableTagId)
                        .ToList();
                    database.Games.Update(game);
                }

                if (plan.GamesToRemove.Count > 0)
                {
                    database.Games.Remove(plan.GamesToRemove);
                }
            }

            return new CloudLibraryReconciliationResult(
                plan.GamesToRemove.Count,
                plan.GamesToMarkUnavailable.Count,
                plan.GamesToMarkAvailable.Count);
        }

        public bool IsSourceUnavailable(Game game)
        {
            return game?.TagIds?.Contains(UnavailableTagId) == true;
        }

        private void EnsureUnavailableTagExists()
        {
            if (database.Tags.Get(UnavailableTagId) != null)
            {
                return;
            }

            database.Tags.Add(new[]
            {
                new Tag(UnavailableTagName)
                {
                    Id = UnavailableTagId
                }
            });
        }
    }

    internal sealed class CloudLibraryReconciliationResult
    {
        public int RemovedGames { get; }
        public int MarkedUnavailable { get; }
        public int MarkedAvailable { get; }
        public bool Changed => RemovedGames > 0 || MarkedUnavailable > 0 || MarkedAvailable > 0;

        public CloudLibraryReconciliationResult(int removedGames, int markedUnavailable, int markedAvailable)
        {
            RemovedGames = removedGames;
            MarkedUnavailable = markedUnavailable;
            MarkedAvailable = markedAvailable;
        }
    }
}
