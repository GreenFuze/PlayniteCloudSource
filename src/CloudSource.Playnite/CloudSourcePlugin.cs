using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Providers.GoogleDrive;
using CloudSource.Playnite.Providers.OneDrive;
using CloudSource.Playnite.GameImport;
using CloudSource.Playnite.Storage;
using CloudSource.Playnite.Installation;
using CloudSource.Playnite.Emulation;
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
        private readonly CloudArchiveClassifier archiveClassifier;
        private readonly CloudGameMetadataFactory metadataFactory;
        private readonly CloudStorageLibraryMigrator libraryMigrator;
        private readonly ProviderRegistry providerRegistry;
        private readonly SourcePackageCatalog packageCatalog;
        private readonly CloudPackageResolver packageResolver;
        private readonly InstallationManifestStore manifestStore;
        private readonly CloudLibraryReconciler libraryReconciler;
        private readonly ManagedArchiveInstaller archiveInstaller;
        private readonly ManagedRomInstaller romInstaller;
        private readonly CloudGameActionManager gameActionManager;
        private readonly EmulatorCompatibilityService emulatorCompatibility;

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
            var googleCredentials = GoogleDriveApplication.CreateCredentials();
            var googleDriveConnection = new GoogleDriveConnectionService(
                httpClient,
                tokenStore,
                googleCredentials);
            var titleNormalizer = new GameTitleNormalizer();
            archiveClassifier = new CloudArchiveClassifier();
            var packageDiscovery = new CloudPackageDiscovery();
            libraryMigrator = new CloudStorageLibraryMigrator(
                PlayniteApi.Database,
                titleNormalizer,
                archiveClassifier);
            var googleDriveApi = new GoogleDriveApiClient(httpClient, googleDriveConnection, packageDiscovery);
            var googleDrivePickerClient = new GoogleDrivePickerClient(
                googleDriveConnection,
                GoogleDriveApplication.CreatePickerConfiguration());
            var googleDriveFolderPicker = new GoogleDriveFolderPickerDialog(
                PlayniteApi.Dialogs,
                googleDrivePickerClient);
            CloudSourceSettingsViewModel settingsViewModel = null;
            var googleDriveProvider = new GoogleDriveProvider(
                () => settingsViewModel.Settings.CreateGoogleDriveProviderConfiguration(),
                googleCredentials.ClientId,
                googleDriveConnection,
                googleDriveApi,
                googleDriveFolderPicker);
            var oneDriveTokenStore = new ProtectedOneDriveTokenStore(
                Path.Combine(GetPluginUserDataPath(), "onedrive.token"),
                PluginId);
            var oneDriveConnection = new OneDriveConnectionService(httpClient, oneDriveTokenStore);
            var oneDriveApi = new OneDriveApiClient(httpClient, oneDriveConnection, packageDiscovery);
            var oneDriveFolderBrowser = new OneDriveFolderBrowser(oneDriveApi);
            var oneDriveFolderPicker = new OneDriveFolderPickerDialog(
                PlayniteApi.Dialogs,
                oneDriveFolderBrowser);
            var oneDriveProvider = new OneDriveProvider(
                () => settingsViewModel.Settings.CreateOneDriveProviderConfiguration(),
                OneDriveApplication.ClientId,
                oneDriveConnection,
                oneDriveApi,
                oneDriveFolderPicker);
            settingsViewModel = new CloudSourceSettingsViewModel(
                this,
                googleDriveProvider,
                oneDriveProvider);
            SettingsViewModel = settingsViewModel;
            providerRegistry = new ProviderRegistry(new ICloudSourceProvider[]
            {
                googleDriveProvider,
                oneDriveProvider
            });
            packageCatalog = new SourcePackageCatalog();
            packageResolver = new CloudPackageResolver(packageCatalog);
            manifestStore = new InstallationManifestStore(GetManagedStorageLayout);
            libraryReconciler = new CloudLibraryReconciler(
                PlayniteApi.Database,
                manifestStore,
                new CloudLibraryReconciliationPlanner());
            metadataFactory = new CloudGameMetadataFactory(titleNormalizer, archiveClassifier, manifestStore);
            gameActionManager = new CloudGameActionManager();
            emulatorCompatibility = new EmulatorCompatibilityService(PlayniteApi.Database, PlayniteApi.Emulation);
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
            romInstaller = new ManagedRomInstaller(GetManagedStorageLayout, providerRegistry, manifestStore);

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
                var scanResults = provider
                    .ScanAsync(args.CancelToken)
                    .GetAwaiter()
                    .GetResult() ?? throw new InvalidOperationException($"Provider '{provider.Id}' returned no scan result collection.");
                foreach (var scanResult in scanResults)
                {
                    args.CancelToken.ThrowIfCancellationRequested();
                    if (scanResult == null ||
                        !string.Equals(scanResult.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Provider '{provider.Id}' returned an invalid scan scope.");
                    }

                    var packages = scanResult.Packages;
                    var importablePackages = packages.Where(archiveClassifier.ShouldImport).ToList();
                    packageCatalog.ReplaceScope(provider.Id, scanResult.AccountId, importablePackages);
                    var skippedPackages = packages.Count - importablePackages.Count;
                    if (skippedPackages > 0)
                    {
                        Logger.Info($"{CloudStorageProduct.DisplayName} skipped {skippedPackages} non-game archive(s).");
                    }

                    authoritativeSnapshots.Add(new AuthoritativeSourceSnapshot(
                        new CloudSourceScope(provider.Id, scanResult.AccountId),
                        importablePackages.Select(package => package.StableId).ToList()));
                    games.AddRange(importablePackages.Select(metadataFactory.Create));
                }
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
            var classification = archiveClassifier.Classify(package);
            if (classification.ContentKind == CloudContentKind.NativePackage && !archiveInstaller.Supports(package.Kind))
            {
                yield break;
            }

            yield return new CloudInstallController(
                args.Game,
                PlayniteApi,
                archiveInstaller,
                romInstaller,
                gameActionManager,
                archiveClassifier,
                emulatorCompatibility,
                package);
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args?.Game == null || args.Game.PluginId != PluginId)
            {
                yield break;
            }

            yield return new CloudUninstallController(args.Game, PlayniteApi, archiveInstaller);
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (args?.Games == null || args.Games.Count != 1)
            {
                yield break;
            }

            var game = args.Games[0];
            if (game.PluginId != PluginId || !game.IsInstalled ||
                !archiveInstaller.CanCompleteExtractedInstaller(game.GameId, game.InstallDirectory))
            {
                yield break;
            }

            yield return new GameMenuItem
            {
                Description = "Complete extracted installer package",
                MenuSection = "Cloud Storage",
                Action = _ => CompleteExtractedInstaller(game)
            };
        }

        private void CompleteExtractedInstaller(Game game)
        {
            try
            {
                InstallationRecord record = null;
                var result = PlayniteApi.Dialogs.ActivateGlobalProgress(
                    progressArgs =>
                    {
                        record = archiveInstaller.CompleteExtractedInstaller(
                            game.GameId,
                            game.InstallDirectory,
                            game.Name,
                            request => CloudInstallController.ConfirmInstaller(PlayniteApi, progressArgs, request),
                            request => CloudInstallController.SelectLaunchTarget(PlayniteApi, progressArgs, request),
                            update => CloudInstallController.UpdateProgress(progressArgs, update),
                            progressArgs.CancelToken);
                        return Task.CompletedTask;
                    },
                    new GlobalProgressOptions($"Completing installation of {game.Name}", true)
                    {
                        IsIndeterminate = false
                    });
                // A visible native installer cannot be safely terminated by the plugin. If it
                // completed successfully, retain its result even if Cancel was clicked meanwhile.
                if ((result.Canceled || result.Error is OperationCanceledException) && record == null) return;
                if (result.Error != null) throw result.Error;
                if (record == null) throw new InvalidOperationException("Installer completion returned no installation record.");
                game.InstallDirectory = record.InstallDirectory;
                game.InstallSize = (ulong)record.Manifest.InstalledSizeBytes;
                game.IsInstalled = true;
                gameActionManager.EnsureEditablePlayAction(game, record);
                PlayniteApi.Database.Games.Update(game);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"Cloud Storage could not complete extracted installer package for {game.Name}.");
                PlayniteApi.Notifications.Add(
                    "cloud-storage-complete-installer-" + game.Id,
                    $"Cloud Storage could not complete {game.Name}: {exception.GetBaseException().Message}",
                    NotificationType.Error);
            }
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
                RepairMissingEditablePlayActions();
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

        private void RepairMissingEditablePlayActions()
        {
            var repaired = 0;
            foreach (var game in PlayniteApi.Database.Games.Where(candidate =>
                candidate.PluginId == PluginId && candidate.IsInstalled))
            {
                var installation = manifestStore.Find(game.GameId, game.InstallDirectory);
                if (installation == null) continue;
                var changed = false;
                if (string.Equals(installation.Manifest.InstallKind, "managed_rom", StringComparison.Ordinal))
                {
                    if (emulatorCompatibility.TryRestorePlan(installation.Manifest, out var emulatorPlan))
                    {
                        changed = emulatorCompatibility.EnsureGamePlatform(game, emulatorPlan);
                        changed = gameActionManager.EnsureEditableEmulatorAction(game, installation, emulatorPlan) || changed;
                    }
                }
                else
                {
                    changed = gameActionManager.EnsureEditablePlayAction(game, installation);
                }

                if (changed)
                {
                    PlayniteApi.Database.Games.Update(game);
                    repaired++;
                }
            }

            if (repaired > 0)
            {
                Logger.Info($"{CloudStorageProduct.DisplayName} added editable Playnite actions to {repaired} existing installation(s).");
            }
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
