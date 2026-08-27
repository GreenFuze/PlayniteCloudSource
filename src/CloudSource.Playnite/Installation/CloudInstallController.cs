using CloudSource.Playnite.Emulation;
using CloudSource.Playnite.GameImport;
using CloudSource.Playnite.Providers;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Installation
{
    internal sealed class CloudInstallController : InstallController
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly IPlayniteAPI playniteApi;
        private readonly ManagedArchiveInstaller installer;
        private readonly ManagedRomInstaller romInstaller;
        private readonly CloudGameActionManager gameActionManager;
        private readonly CloudArchiveClassifier classifier;
        private readonly EmulatorCompatibilityService emulatorCompatibility;
        private readonly SourcePackage package;

        public CloudInstallController(
            Game game,
            IPlayniteAPI playniteApi,
            ManagedArchiveInstaller installer,
            ManagedRomInstaller romInstaller,
            CloudGameActionManager gameActionManager,
            CloudArchiveClassifier classifier,
            EmulatorCompatibilityService emulatorCompatibility,
            SourcePackage package)
            : base(game)
        {
            this.playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            this.installer = installer ?? throw new ArgumentNullException(nameof(installer));
            this.romInstaller = romInstaller ?? throw new ArgumentNullException(nameof(romInstaller));
            this.gameActionManager = gameActionManager ?? throw new ArgumentNullException(nameof(gameActionManager));
            this.classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
            this.emulatorCompatibility = emulatorCompatibility ?? throw new ArgumentNullException(nameof(emulatorCompatibility));
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            Name = "Install from Cloud Storage";
        }

        public override void Install(InstallActionArgs args)
        {
            try
            {
                var classification = classifier.Classify(package);
                EmulatorInstallPlan emulatorPlan = null;
                if (classification.ContentKind == CloudContentKind.Rom)
                {
                    emulatorPlan = SelectEmulatorPlan(classification);
                    if (emulatorPlan == null)
                    {
                        InvokeOnInstallationCancelled(new GameInstallationCancelledEventArgs());
                        return;
                    }
                }

                InstallationRecord record = null;
                var result = playniteApi.Dialogs.ActivateGlobalProgress(
                    progressArgs =>
                    {
                        record = classification.ContentKind == CloudContentKind.Rom
                            ? romInstaller.Install(
                                package,
                                Game.Name,
                                emulatorPlan,
                                update => UpdateProgress(progressArgs, update),
                                progressArgs.CancelToken)
                            : installer.Install(
                                package,
                                Game.Name,
                                request => ConfirmInstaller(playniteApi, progressArgs, request),
                                request => SelectLaunchTarget(playniteApi, progressArgs, request),
                                update => UpdateProgress(progressArgs, update),
                                progressArgs.CancelToken);
                        return Task.CompletedTask;
                    },
                    new GlobalProgressOptions($"Installing {Game.Name}", true)
                    {
                        IsIndeterminate = false
                    });

                // Cancellation remains effective while preparing the package. Once a native
                // installer has completed and returned a record, Playnite must accept that
                // result instead of leaving an installed game unregistered.
                if ((result.Canceled || result.Error is OperationCanceledException) && record == null)
                {
                    InvokeOnInstallationCancelled(new GameInstallationCancelledEventArgs());
                    return;
                }

                if (result.Error != null)
                {
                    throw result.Error;
                }

                if (record == null)
                {
                    throw new InvalidOperationException("Cloud Storage installation finished without an installation record.");
                }

                var databaseGame = playniteApi.Database.Games.Get(Game.Id) ??
                    throw new InvalidOperationException("The installed game is no longer present in the Playnite database.");
                var gameChanged = false;
                if (classification.ContentKind == CloudContentKind.Rom)
                {
                    emulatorCompatibility.EnsureGamePlatform(databaseGame, emulatorPlan);
                    gameChanged = gameActionManager.EnsureEditableEmulatorAction(databaseGame, record, emulatorPlan);
                }
                else
                {
                    gameChanged = gameActionManager.EnsureEditablePlayAction(databaseGame, record);
                }
                if (gameChanged || classification.ContentKind == CloudContentKind.Rom)
                    playniteApi.Database.Games.Update(databaseGame);

                InvokeOnInstalled(new GameInstalledEventArgs(new GameInstallationData
                {
                    InstallDirectory = record.InstallDirectory
                }));
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"Cloud Storage failed to install {Game.Name}.");
                playniteApi.Notifications.Add(
                    "cloud-storage-install-" + Game.Id,
                    $"Cloud Storage could not install {Game.Name}: {exception.GetBaseException().Message}",
                    NotificationType.Error);
                InvokeOnInstallationCancelled(new GameInstallationCancelledEventArgs());
            }
        }

        private EmulatorInstallPlan SelectEmulatorPlan(CloudGameClassification classification)
        {
            var candidates = emulatorCompatibility.FindCompatibleProfiles(Game, classification, package);
            if (candidates.Count == 0)
            {
                playniteApi.Dialogs.ShowMessage(
                    $"{Game.Name} requires an emulator for {classification.PlatformName}, but Playnite has no configured " +
                    $"profile for {DescribeEmulatorInput(package)}.\n\n" +
                    "Configure an emulator in Playnite's Emulators settings, then try Install again. Nothing was downloaded.",
                    "Emulator required",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return null;
            }

            if (candidates.Count == 1) return candidates[0];
            var options = candidates.Select(candidate => new GenericItemOption
            {
                Name = candidate.DisplayName,
                Description = $"{candidate.PlatformName} emulator profile"
            }).ToList();
            List<GenericItemOption> Search(string query)
            {
                if (string.IsNullOrWhiteSpace(query)) return options;
                return options.Where(option =>
                    option.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            var selected = playniteApi.Dialogs.ChooseItemWithSearch(
                options,
                Search,
                caption: $"Choose emulator for {Game.Name}");
            if (selected == null) return null;
            var selectedIndex = options.IndexOf(selected);
            if (selectedIndex < 0) throw new InvalidOperationException("Playnite returned an unknown emulator selection.");
            return candidates[selectedIndex];
        }

        private static string DescribeEmulatorInput(SourcePackage sourcePackage)
        {
            switch (sourcePackage.Kind)
            {
                case SourcePackageKind.ScummVmDirectory: return ".scummvm game directories";
                case SourcePackageKind.MsDosDirectory: return ".jsdos game directories";
                default:
                    return "." + System.IO.Path.GetExtension(sourcePackage.DisplayName).TrimStart('.') + " files";
            }
        }

        internal static void UpdateProgress(
            GlobalProgressActionArgs progressArgs,
            InstallationProgressUpdate update)
        {
            switch (update.Stage)
            {
                case InstallationProgressStage.Downloading:
                    progressArgs.IsIndeterminate = false;
                    if (update.TotalBytes > 0)
                    {
                        progressArgs.ProgressMaxValue = update.TotalBytes;
                        progressArgs.CurrentProgressValue = update.CompletedBytes;
                        progressArgs.Text = $"Downloading from Cloud Storage… {FormatPercentage(update)} — {FormatBytes(update.CompletedBytes)} / {FormatBytes(update.TotalBytes)}";
                    }
                    else if (update.CompletedBytes > 0)
                    {
                        progressArgs.Text = $"Downloading from Cloud Storage… {FormatBytes(update.CompletedBytes)}";
                    }
                    else
                    {
                        progressArgs.ProgressMaxValue = 1;
                        progressArgs.CurrentProgressValue = 0;
                        progressArgs.Text = "Connecting to Cloud Storage… 0%";
                    }
                    break;
                case InstallationProgressStage.Extracting:
                    SetDeterminateProgress(progressArgs, update, capBeforeCompletion: true);
                    progressArgs.Text = $"Extracting game archive… {FormatPercentage(update, capBeforeCompletion: true)}";
                    break;
                case InstallationProgressStage.RunningInstaller:
                    SetDeterminateProgress(progressArgs, update);
                    progressArgs.Text = "Installer running in a separate window…";
                    break;
                case InstallationProgressStage.ValidatingInstallation:
                    SetDeterminateProgress(progressArgs, update);
                    progressArgs.Text = $"Validating installed game… {FormatPercentage(update)}";
                    break;
                case InstallationProgressStage.Finalizing:
                    SetDeterminateProgress(progressArgs, update);
                    progressArgs.Text = $"Finalizing installation… {FormatPercentage(update)}";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(update.Stage), update.Stage, "Unknown installation progress stage.");
            }
        }

        private static void SetDeterminateProgress(
            GlobalProgressActionArgs progressArgs,
            InstallationProgressUpdate update,
            bool capBeforeCompletion = false)
        {
            progressArgs.IsIndeterminate = false;
            progressArgs.ProgressMaxValue = Math.Max(1, update.TotalBytes);
            var completed = Math.Min(update.CompletedBytes, progressArgs.ProgressMaxValue);
            if (capBeforeCompletion && completed >= progressArgs.ProgressMaxValue)
            {
                completed = progressArgs.ProgressMaxValue * 0.999;
            }

            progressArgs.CurrentProgressValue = completed;
        }

        private static string FormatPercentage(InstallationProgressUpdate update, bool capBeforeCompletion = false)
        {
            if (update.TotalBytes <= 0)
            {
                return "0%";
            }

            var percentage = Math.Min(100d, update.CompletedBytes * 100d / update.TotalBytes);
            if (capBeforeCompletion) percentage = Math.Min(99.9d, percentage);
            return $"{percentage:0.0}%";
        }

        internal static bool ConfirmInstaller(
            IPlayniteAPI playniteApi,
            GlobalProgressActionArgs progressArgs,
            InstallerConfirmationRequest request)
        {
            var signer = string.IsNullOrWhiteSpace(request.SignerSubject)
                ? "Embedded signer: none (publisher not verified)"
                : "Embedded signer: " + request.SignerSubject + " (trust not automatically verified)";
            var message =
                $"Cloud Storage found an Inno Setup installer for {request.GameName}.\n\n" +
                $"Installer: {request.InstallerName}\n" +
                $"{signer}\n" +
                $"Managed destination: {request.Destination}\n\n" +
                "The installer will run visibly and may request administrator permission. " +
                "Keep the managed destination unchanged. Run this installer?";
            return progressArgs.MainDispatcher.Invoke(() =>
                playniteApi.Dialogs.ShowMessage(
                    message,
                    "Run game installer",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes);
        }

        internal static string SelectLaunchTarget(
            IPlayniteAPI playniteApi,
            GlobalProgressActionArgs progressArgs,
            LaunchTargetSelectionRequest request)
        {
            return progressArgs.MainDispatcher.Invoke(() =>
            {
                var options = request.Candidates
                    .Select(candidate => new GenericItemOption
                    {
                        Name = candidate.FileName,
                        Description = candidate.RelativePath
                    })
                    .ToList();
                List<GenericItemOption> Search(string query)
                {
                    if (string.IsNullOrWhiteSpace(query)) return options;
                    return options.Where(option =>
                        option.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        option.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                }

                var selected = playniteApi.Dialogs.ChooseItemWithSearch(
                    options,
                    Search,
                    caption: $"Choose launcher for {request.GameName}");
                return selected?.Description;
            });
        }

        private static string FormatBytes(long bytes)
        {
            const double megabyte = 1024d * 1024d;
            const double gigabyte = 1024d * 1024d * 1024d;
            return bytes >= gigabyte
                ? $"{bytes / gigabyte:0.00} GB"
                : $"{bytes / megabyte:0.0} MB";
        }
    }
}
