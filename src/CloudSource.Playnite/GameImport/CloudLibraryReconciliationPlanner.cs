using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudSource.Playnite.GameImport
{
    internal sealed class CloudLibraryReconciliationPlanner
    {
        public CloudLibraryReconciliationPlan CreatePlan(
            IEnumerable<Game> games,
            Guid pluginId,
            CloudSourceScope scope,
            IEnumerable<string> authoritativeGameIds,
            Func<Game, bool> hasManagedInstallation,
            Guid unavailableTagId)
        {
            if (games == null) throw new ArgumentNullException(nameof(games));
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (authoritativeGameIds == null) throw new ArgumentNullException(nameof(authoritativeGameIds));
            if (hasManagedInstallation == null) throw new ArgumentNullException(nameof(hasManagedInstallation));
            if (unavailableTagId == Guid.Empty) throw new ArgumentException("Unavailable tag ID is required.", nameof(unavailableTagId));

            var availableIds = new HashSet<string>(authoritativeGameIds, StringComparer.Ordinal);
            var plan = new CloudLibraryReconciliationPlan();
            foreach (var game in games.Where(game => game.PluginId == pluginId))
            {
                if (!CloudGameIdentity.TryParse(game.GameId, out var identity) || !identity.BelongsTo(scope))
                {
                    continue;
                }

                var isTaggedUnavailable = game.TagIds?.Contains(unavailableTagId) == true;
                if (availableIds.Contains(identity.StableId))
                {
                    if (isTaggedUnavailable)
                    {
                        plan.GamesToMarkAvailable.Add(game);
                    }

                    continue;
                }

                if (game.IsInstalled || hasManagedInstallation(game))
                {
                    if (!isTaggedUnavailable)
                    {
                        plan.GamesToMarkUnavailable.Add(game);
                    }
                }
                else
                {
                    plan.GamesToRemove.Add(game);
                }
            }

            return plan;
        }
    }

    internal sealed class CloudLibraryReconciliationPlan
    {
        public List<Game> GamesToRemove { get; } = new List<Game>();
        public List<Game> GamesToMarkUnavailable { get; } = new List<Game>();
        public List<Game> GamesToMarkAvailable { get; } = new List<Game>();
    }
}
