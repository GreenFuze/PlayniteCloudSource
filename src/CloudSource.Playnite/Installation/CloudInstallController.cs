using CloudSource.Playnite.Providers;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Threading;
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

        public override async void Install(InstallActionArgs args)
        {
            try
            {
                var record = await Task.Run(() => installer.Install(package, Game.Name, CancellationToken.None));
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
    }
}
