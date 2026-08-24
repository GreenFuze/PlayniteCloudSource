using CloudSource.Playnite.Emulation;
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

        public bool EnsureEditableEmulatorAction(
            Game game,
            InstallationRecord installation,
            EmulatorInstallPlan emulatorPlan)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (installation == null) throw new ArgumentNullException(nameof(installation));
            if (emulatorPlan == null) throw new ArgumentNullException(nameof(emulatorPlan));
            if (string.IsNullOrWhiteSpace(installation.Manifest.RomTarget))
                throw new InvalidDataException("The managed ROM installation has no ROM target.");

            var romPath = Path.Combine(ExpandableVariables.InstallationDirectory, installation.Manifest.RomTarget);
            var changed = false;
            if (game.Roms == null)
            {
                game.Roms = new ObservableCollection<GameRom>();
            }

            if (!game.Roms.Any(rom => string.Equals(rom.Path, romPath, StringComparison.OrdinalIgnoreCase)))
            {
                game.Roms.Add(new GameRom(game.Name, romPath));
                changed = true;
            }

            if (game.GameActions == null)
            {
                game.GameActions = new ObservableCollection<GameAction>();
            }

            if (game.GameActions.Any(candidate =>
                candidate.IsPlayAction &&
                string.Equals(candidate.Name, ManagedActionName, StringComparison.Ordinal)))
            {
                return changed;
            }

            game.GameActions.Add(new GameAction
            {
                Name = ManagedActionName,
                IsPlayAction = true,
                Type = GameActionType.Emulator,
                EmulatorId = emulatorPlan.EmulatorId,
                EmulatorProfileId = emulatorPlan.EmulatorProfileId,
                AdditionalArguments = emulatorPlan.AdditionalArguments
            });
            return true;
        }
    }
}
