using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Threading.Tasks;

namespace CloudSource.Playnite.Installation
{
    internal sealed class CloudUninstallController : UninstallController
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly IPlayniteAPI playniteApi;
        private readonly ManagedArchiveInstaller installer;

        public CloudUninstallController(Game game, IPlayniteAPI playniteApi, ManagedArchiveInstaller installer)
            : base(game)
        {
            this.playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            this.installer = installer ?? throw new ArgumentNullException(nameof(installer));
            Name = "Uninstall managed Cloud Storage copy";
        }

        public override async void Uninstall(UninstallActionArgs args)
        {
            try
            {
                await Task.Run(() => installer.Uninstall(Game.GameId, Game.InstallDirectory));
                InvokeOnUninstalled();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"Cloud Storage refused to uninstall {Game.Name}.");
                playniteApi.Notifications.Add(
                    "cloud-storage-uninstall-" + Game.Id,
                    $"Cloud Storage did not remove {Game.Name}: {exception.GetBaseException().Message}",
                    NotificationType.Error);
            }
        }
    }
}
