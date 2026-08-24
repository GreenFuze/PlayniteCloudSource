using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace CloudSource.Playnite.Installation
{
    internal sealed class CloudGameActionManager
    {
        internal const string ManagedActionName = "Play (Cloud Storage)";

        public bool EnsureEditablePlayAction(Game game, InstallationRecord installation)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (installation == null) throw new ArgumentNullException(nameof(installation));
            if (string.IsNullOrWhiteSpace(installation.Manifest.LaunchTarget))
                throw new InvalidDataException("The managed installation has no launcher.");

            var actionPath = Path.Combine(
                ExpandableVariables.InstallationDirectory,
                installation.Manifest.LaunchTarget);
            var relativeWorkingDirectory = Path.GetDirectoryName(installation.Manifest.LaunchTarget);
            var workingDirectory = string.IsNullOrWhiteSpace(relativeWorkingDirectory)
                ? ExpandableVariables.InstallationDirectory
                : Path.Combine(ExpandableVariables.InstallationDirectory, relativeWorkingDirectory);

            if (game.GameActions == null)
            {
                game.GameActions = new ObservableCollection<GameAction>();
            }

            var managedAction = game.GameActions.FirstOrDefault(candidate =>
                candidate.IsPlayAction &&
                string.Equals(candidate.Name, ManagedActionName, StringComparison.Ordinal));
            if (managedAction != null)
            {
                return false;
            }

            if (game.GameActions.Any(candidate =>
                candidate.IsPlayAction &&
                candidate.Type == GameActionType.File &&
                string.Equals(candidate.Path, actionPath, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            game.GameActions.Add(new GameAction
            {
                Name = ManagedActionName,
                IsPlayAction = true,
                Type = GameActionType.File,
                Path = actionPath,
                WorkingDir = workingDirectory,
                TrackingMode = TrackingMode.Default
            });
            return true;
        }
    }
}
