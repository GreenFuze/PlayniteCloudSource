using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Providers.GoogleDrive;
using CloudSource.Playnite.GameImport;
using CloudSource.Playnite.Storage;
using CloudSource.Playnite.Installation;
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
        private readonly GoogleDriveFolderPickerDialog googleDriveFolderPicker;
        private readonly CloudArchiveClassifier archiveClassifier;
        private readonly CloudGameMetadataFactory metadataFactory;
        private readonly CloudStorageLibraryMigrator libraryMigrator;
        private readonly ProviderRegistry providerRegistry;
        private readonly CloudPackageResolver packageResolver;
        private readonly InstallationManifestStore manifestStore;
        private readonly CloudLibraryReconciler libraryReconciler;
        private readonly ManagedArchiveInstaller archiveInstaller;

        public static readonly Guid PluginId = Guid.Parse("a6fd3d1b-450e-4c8b-8476-ce14ad3ab3c2");

        public override Guid Id => PluginId;
        public override string Name => CloudStorageProduct.DisplayName;
        public override string LibraryIcon => Path.Combine(
            Path.GetDirectoryName(typeof(CloudSourcePlugin).Assembly.Location),
            "Resources",
            "cloud-storage.png");
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
            var titleNormalizer = new GameTitleNormalizer();
            archiveClassifier = new CloudArchiveClassifier();
            libraryMigrator = new CloudStorageLibraryMigrator(
                PlayniteApi.Database,
                titleNormalizer,
                archiveClassifier);
            SettingsViewModel = new CloudSourceSettingsViewModel(this);
            var googleDriveApi = new GoogleDriveApiClient(httpClient, googleDriveConnection);
            var googleDriveFolderBrowser = new GoogleDriveFolderBrowser(googleDriveApi);
            googleDriveFolderPicker = new GoogleDriveFolderPickerDialog(
                PlayniteApi.Dialogs,
                googleDriveFolderBrowser);
            var googleDriveProvider = new GoogleDriveProvider(
                () => SettingsViewModel.Settings.CreateGoogleDriveConfiguration(),
                () => SettingsViewModel.Settings.GoogleDriveEnabled &&
                      SettingsViewModel.Settings.HasConcreteGoogleDriveFolder,
                googleDriveConnection,
                googleDriveApi);
            providerRegistry = new ProviderRegistry(new[] { googleDriveProvider });
            packageResolver = new CloudPackageResolver();
            manifestStore = new InstallationManifestStore(GetManagedStorageLayout);
            libraryReconciler = new CloudLibraryReconciler(
                PlayniteApi.Database,
                manifestStore,
                new CloudLibraryReconciliationPlanner());
            metadataFactory = new CloudGameMetadataFactory(titleNormalizer, archiveClassifier, manifestStore);
            archiveInstaller = new ManagedArchiveInstaller(
                GetManagedStorageLayout,
                providerRegistry,
                new ArchiveExtractorRegistry(new IArchiveExtractor[]
                {
                    new SafeZipExtractor(),
                    new SafeSharpCompressExtractor(SourcePackageKind.SevenZipArchive),
                    new SafeSharpCompressExtractor(SourcePackageKind.RarArchive)
                }),
                new LaunchTargetResolver(titleNormalizer),
                manifestStore);

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
            Logger.Info($"{CloudStorageProduct.DisplayName} scan requested with {configuredProviders.Count} configured provider(s).");
            if (configuredProviders.Count == 0)
            {
                return Enumerable.Empty<GameMetadata>();
            }

            var games = new List<GameMetadata>();
            var authoritativeSnapshots = new List<AuthoritativeSourceSnapshot>();
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
                    var importablePackages = packages.Where(archiveClassifier.ShouldImport).ToList();
                    var skippedPackages = packages.Count - importablePackages.Count;
                    if (skippedPackages > 0)
                    {
                        Logger.Info($"{CloudStorageProduct.DisplayName} skipped {skippedPackages} non-game archive(s).");
                    }

                    authoritativeSnapshots.Add(new AuthoritativeSourceSnapshot(
                        new CloudSourceScope(provider.Id, settings.GoogleDriveAccountId),
                        importablePackages.Select(package => package.StableId).ToList()));
                    games.AddRange(importablePackages.Select(metadataFactory.Create));
                    continue;
                }

                throw new InvalidOperationException($"Provider '{provider.Id}' has no Playnite import adapter.");
            }

            args.CancelToken.ThrowIfCancellationRequested();
            foreach (var snapshot in authoritativeSnapshots)
            {
                var reconciliation = libraryReconciler.Reconcile(snapshot);
                if (reconciliation.Changed)
                {
                    Logger.Info(
                        $"{CloudStorageProduct.DisplayName} reconciliation removed {reconciliation.RemovedGames} " +
                        $"missing uninstalled game(s), marked {reconciliation.MarkedUnavailable} installed game(s) " +
                        $"unavailable, and restored {reconciliation.MarkedAvailable} game(s).");
                }
            }

            Logger.Info($"{CloudStorageProduct.DisplayName} discovered {games.Count} game archive package(s).");
            return games;
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return SettingsViewModel;
        }

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args?.Game == null || args.Game.PluginId != PluginId)
            {
                yield break;
            }

            if (libraryReconciler.IsSourceUnavailable(args.Game))
            {
                yield break;
            }

            var package = packageResolver.Resolve(args.Game);
            if (!archiveInstaller.Supports(package.Kind))
            {
                yield break;
            }

            yield return new CloudInstallController(args.Game, PlayniteApi, archiveInstaller, package);
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args?.Game == null || args.Game.PluginId != PluginId)
            {
                yield break;
            }

            yield return new CloudUninstallController(args.Game, PlayniteApi, archiveInstaller);
        }

        public override IEnumerable<PlayController> GetPlayActions(GetPlayActionsArgs args)
        {
            if (args?.Game == null || args.Game.PluginId != PluginId)
            {
                yield break;
            }

            var installation = manifestStore.Find(args.Game.GameId, args.Game.InstallDirectory);
            if (installation == null)
            {
                yield break;
            }

            yield return new AutomaticPlayController(args.Game)
            {
                Name = "Play managed Cloud Storage copy",
                Type = AutomaticPlayActionType.File,
                Path = installation.LaunchPath,
                WorkingDir = Path.GetDirectoryName(installation.LaunchPath),
                TrackingMode = TrackingMode.Default
            };
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
                Logger.Error($"{CloudStorageProduct.DisplayName} managed root is invalid: {error}");
                return;
            }

            try
            {
                layout.EnsureCreated();
                Logger.Info($"{CloudStorageProduct.DisplayName} managed root ready at {layout.RootPath}.");
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"{CloudStorageProduct.DisplayName} could not initialize its managed root.");
            }

            var migration = libraryMigrator.Migrate();
            if (migration.Changed)
            {
                Logger.Info(
                    $"{CloudStorageProduct.DisplayName} migrated {migration.RenamedSources} source(s), " +
                    $"cleaned {migration.CleanedTitles} title(s), and assigned {migration.AssignedPlatforms} platform(s).");
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

        internal GoogleDriveFolder ShowGoogleDriveFolderPicker(
            GoogleDriveAccountConfiguration configuration,
            GoogleDriveAuthorization draftAuthorization,
            string existingSelectionPath)
        {
            return googleDriveFolderPicker.Show(
                configuration,
                draftAuthorization,
                existingSelectionPath);
        }

        internal void ShowError(string message)
        {
            PlayniteApi.Dialogs.ShowErrorMessage(message, CloudStorageProduct.DisplayName);
        }

        private ManagedStorageLayout GetManagedStorageLayout()
        {
            if (!ManagedStorageLayout.TryCreate(
                SettingsViewModel.Settings.ManagedRootPath,
                out var layout,
                out var error))
            {
                throw new InvalidOperationException(error);
            }

            return layout;
        }
    }
}
