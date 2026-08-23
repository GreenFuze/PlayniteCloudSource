using CloudSource.Playnite.Providers;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Installation
{
    internal sealed class CloudInstallController : InstallController
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly IPlayniteAPI playniteApi;
        private readonly ManagedArchiveInstaller installer;
        private readonly SourcePackage package;

        public CloudInstallController(Game game, IPlayniteAPI playniteApi, ManagedArchiveInstaller installer, SourcePackage package)
            : base(game)
        {
            this.playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            this.installer = installer ?? throw new ArgumentNullException(nameof(installer));
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            Name = "Install from Cloud Storage";
        }

        public override void Install(InstallActionArgs args)
        {
            try
            {
                InstallationRecord record = null;
                var result = playniteApi.Dialogs.ActivateGlobalProgress(
                    progressArgs =>
                    {
                        record = installer.Install(
                            package,
                            Game.Name,
                            update => UpdateProgress(progressArgs, update),
                            progressArgs.CancelToken);
                        return Task.CompletedTask;
                    },
                    new GlobalProgressOptions($"Installing {Game.Name}", true)
                    {
                        IsIndeterminate = true
                    });

                if (result.Canceled || result.Error is OperationCanceledException)
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

        private static void UpdateProgress(
            GlobalProgressActionArgs progressArgs,
            InstallationProgressUpdate update)
        {
            switch (update.Stage)
            {
                case InstallationProgressStage.Downloading:
                    progressArgs.IsIndeterminate = update.TotalBytes <= 0;
                    if (update.TotalBytes > 0)
                    {
                        progressArgs.ProgressMaxValue = update.TotalBytes;
                        progressArgs.CurrentProgressValue = update.CompletedBytes;
                        progressArgs.Text = $"Downloading from Cloud Storage… {FormatBytes(update.CompletedBytes)} / {FormatBytes(update.TotalBytes)}";
                    }
                    else if (update.CompletedBytes > 0)
                    {
                        progressArgs.Text = $"Downloading from Cloud Storage… {FormatBytes(update.CompletedBytes)}";
                    }
                    else
                    {
                        progressArgs.Text = "Downloading from Cloud Storage…";
                    }
                    break;
                case InstallationProgressStage.Extracting:
                    progressArgs.IsIndeterminate = true;
                    progressArgs.Text = "Extracting game archive…";
                    break;
                case InstallationProgressStage.Finalizing:
                    progressArgs.IsIndeterminate = true;
                    progressArgs.Text = "Finalizing installation…";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(update.Stage), update.Stage, "Unknown installation progress stage.");
            }
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
