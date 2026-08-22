using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Providers.GoogleDrive;
using CloudSource.Playnite.Storage;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace CloudSource.Playnite
{
    public sealed class CloudSourcePlugin : LibraryPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly HttpClient httpClient;
        private readonly GoogleDriveConnectionService googleDriveConnection;
        private readonly ProviderRegistry providerRegistry;

        public static readonly Guid PluginId = Guid.Parse("a6fd3d1b-450e-4c8b-8476-ce14ad3ab3c2");

        public override Guid Id => PluginId;
        public override string Name => "Cloud Source";
        public CloudSourceSettingsViewModel SettingsViewModel { get; }

        public CloudSourcePlugin(IPlayniteAPI playniteApi)
            : base(playniteApi ?? throw new ArgumentNullException(nameof(playniteApi)))
        {
            httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            var tokenStore = new ProtectedGoogleDriveTokenStore(
                Path.Combine(GetPluginUserDataPath(), "google-drive.token"),
                PluginId);
            googleDriveConnection = new GoogleDriveConnectionService(httpClient, tokenStore);
            SettingsViewModel = new CloudSourceSettingsViewModel(this);
            var googleDriveApi = new GoogleDriveApiClient(httpClient, googleDriveConnection);
            var googleDriveProvider = new GoogleDriveProvider(
                () => SettingsViewModel.Settings.CreateGoogleDriveConfiguration(),
                () => SettingsViewModel.Settings.GoogleDriveEnabled,
                googleDriveConnection,
                googleDriveApi);
            providerRegistry = new ProviderRegistry(new[] { googleDriveProvider });

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

            var configuredProviders = providerRegistry.GetConfiguredProviders();
            Logger.Info($"Cloud Source scan requested with {configuredProviders.Count} configured provider(s).");
            if (configuredProviders.Count == 0)
            {
                return Enumerable.Empty<GameMetadata>();
            }

            var games = new List<GameMetadata>();
            foreach (var provider in configuredProviders)
            {
                args.CancelToken.ThrowIfCancellationRequested();
                if (provider.Id == GoogleDriveProvider.ProviderId)
                {
                    var settings = SettingsViewModel.Settings;
                    var request = new SourceScanRequest(
                        settings.GoogleDriveAccountId,
                        new[]
                        {
                            new SourceLocation(
                                settings.GoogleDriveFolderId,
                                settings.GoogleDriveFolderDisplayPath,
                                recursive: true)
                        });
                    var packages = provider
                        .ScanAsync(request, args.CancelToken)
                        .GetAwaiter()
                        .GetResult();
                    games.AddRange(packages.Select(ToGameMetadata));
                    continue;
                }

                throw new InvalidOperationException($"Provider '{provider.Id}' has no Playnite import adapter.");
            }

            Logger.Info($"Cloud Source discovered {games.Count} archive package(s).");
            return games;
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

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            httpClient.Dispose();
        }

        internal Task<GoogleDriveAuthorization> AuthorizeGoogleDriveAsync(
            string clientId,
            string clientSecret,
            CancellationToken cancellationToken)
        {
            return googleDriveConnection.AuthorizeAsync(clientId, clientSecret, cancellationToken);
        }

        internal void CommitGoogleDriveAuthorization(GoogleDriveAuthorization authorization)
        {
            googleDriveConnection.Commit(authorization);
        }

        internal void DisconnectGoogleDrive()
        {
            googleDriveConnection.Disconnect();
        }

        internal bool HasGoogleDriveAuthorization => googleDriveConnection.HasStoredAuthorization;

        internal void ShowError(string message)
        {
            PlayniteApi.Dialogs.ShowErrorMessage(message, "Cloud Source");
        }

        private static GameMetadata ToGameMetadata(SourcePackage package)
        {
            var name = Path.GetFileNameWithoutExtension(package.DisplayName);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException($"Cloud package '{package.StableId}' has no usable game name.");
            }

            return new GameMetadata
            {
                GameId = package.StableId,
                Name = name,
                Description = $"Cloud archive: {package.LogicalPath}",
                IsInstalled = false,
                Source = new MetadataNameProperty("Cloud Source"),
                Version = package.Revision
            };
        }
    }
}
