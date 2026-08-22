using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Storage;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace CloudSource.Playnite
{
    public sealed class CloudSourcePlugin : LibraryPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly ProviderRegistry providerRegistry;

        public static readonly Guid PluginId = Guid.Parse("a6fd3d1b-450e-4c8b-8476-ce14ad3ab3c2");

        public override Guid Id => PluginId;
        public override string Name => "Cloud Source";
        public CloudSourceSettingsViewModel SettingsViewModel { get; }

        public CloudSourcePlugin(IPlayniteAPI playniteApi)
            : this(playniteApi, new ProviderRegistry(Enumerable.Empty<ICloudSourceProvider>()))
        {
        }

        internal CloudSourcePlugin(IPlayniteAPI playniteApi, ProviderRegistry providerRegistry)
            : base(playniteApi)
        {
            this.providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
            SettingsViewModel = new CloudSourceSettingsViewModel(this);
            Properties = new LibraryPluginProperties
            {
                HasSettings = true,
                HasCustomizedGameImport = false
            };
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            Logger.Info($"Cloud Source scan requested with {providerRegistry.Count} registered provider(s).");
            return Enumerable.Empty<GameMetadata>();
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return SettingsViewModel;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new CloudSourceSettingsView();
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (!ManagedStorageLayout.TryCreate(
                SettingsViewModel.Settings.ManagedRootPath,
                out var layout,
                out var error))
            {
                Logger.Error($"Cloud Source managed root is invalid: {error}");
                return;
            }

            try
            {
                layout.EnsureCreated();
                Logger.Info($"Cloud Source managed root ready at {layout.RootPath}.");
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Cloud Source could not initialize its managed root.");
            }
        }
    }
}
