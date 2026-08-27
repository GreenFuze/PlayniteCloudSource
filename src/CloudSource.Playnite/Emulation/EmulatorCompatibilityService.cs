using CloudSource.Playnite.GameImport;
using CloudSource.Playnite.Installation;
using CloudSource.Playnite.Providers;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CloudSource.Playnite.Emulation
{
    internal sealed class EmulatorInstallPlan
    {
        public string PlatformName { get; }
        public string PlatformSpecificationId { get; }
        public Guid EmulatorId { get; }
        public string EmulatorName { get; }
        public string EmulatorProfileId { get; }
        public string EmulatorProfileName { get; }
        public string ImageExtension { get; }
        public string AdditionalArguments { get; }

        public string DisplayName => $"{EmulatorName} — {EmulatorProfileName}";

        public EmulatorInstallPlan(
            string platformName,
            string platformSpecificationId,
            Guid emulatorId,
            string emulatorName,
            string emulatorProfileId,
            string emulatorProfileName,
            string imageExtension,
            string additionalArguments)
        {
            PlatformName = Required(platformName, nameof(platformName));
            PlatformSpecificationId = Required(platformSpecificationId, nameof(platformSpecificationId));
            EmulatorId = emulatorId != Guid.Empty ? emulatorId : throw new ArgumentException("Emulator ID is required.", nameof(emulatorId));
            EmulatorName = Required(emulatorName, nameof(emulatorName));
            EmulatorProfileId = Required(emulatorProfileId, nameof(emulatorProfileId));
            EmulatorProfileName = Required(emulatorProfileName, nameof(emulatorProfileName));
            ImageExtension = Required(imageExtension, nameof(imageExtension)).TrimStart('.').ToLowerInvariant();
            AdditionalArguments = additionalArguments;
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }

    internal sealed class EmulatorCompatibilityService
    {
        private readonly IGameDatabase database;
        private readonly IEmulationAPI emulationApi;

        public EmulatorCompatibilityService(IGameDatabase database, IEmulationAPI emulationApi)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.emulationApi = emulationApi ?? throw new ArgumentNullException(nameof(emulationApi));
        }

        public IReadOnlyList<EmulatorInstallPlan> FindCompatibleProfiles(
            Game game,
            CloudGameClassification classification,
            SourcePackage package)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (classification == null) throw new ArgumentNullException(nameof(classification));
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (classification.ContentKind != CloudContentKind.Rom)
                throw new InvalidOperationException("Emulator compatibility can only be evaluated for ROM content.");

            var platform = ResolvePlatform(classification);
            var extensions = GetCandidateExtensions(package);
            if (extensions.Count == 0)
                throw new InvalidDataException("The ROM file has no extension to match against an emulator profile.");
            var requireDeclaredExtension = SourcePackage.IsDirectoryPackage(package.Kind);

            var databasePlatform = database.Platforms.FirstOrDefault(candidate =>
                string.Equals(candidate.SpecificationId, platform.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Name, platform.Name, StringComparison.OrdinalIgnoreCase));
            var matches = new List<EmulatorInstallPlan>();
            foreach (var emulator in database.Emulators)
            {
                AddBuiltInMatches(matches, emulator, platform, extensions, requireDeclaredExtension);
                AddCustomMatches(matches, emulator, databasePlatform, platform, extensions, requireDeclaredExtension);
            }

            return matches
                .OrderBy(match => match.EmulatorName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(match => match.EmulatorProfileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool EnsureGamePlatform(Game game, EmulatorInstallPlan plan)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var platform = database.Platforms.FirstOrDefault(candidate =>
                string.Equals(candidate.SpecificationId, plan.PlatformSpecificationId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Name, plan.PlatformName, StringComparison.OrdinalIgnoreCase));
            if (platform == null)
            {
                platform = new Platform(plan.PlatformName) { SpecificationId = plan.PlatformSpecificationId };
                database.Platforms.Add(platform);
            }
            else if (string.IsNullOrWhiteSpace(platform.SpecificationId))
            {
                platform.SpecificationId = plan.PlatformSpecificationId;
                database.Platforms.Update(platform);
            }

            if (game.PlatformIds == null) game.PlatformIds = new List<Guid>();
            if (game.PlatformIds.Contains(platform.Id)) return false;
            game.PlatformIds.Add(platform.Id);
            return true;
        }

        public bool TryRestorePlan(InstallManifest manifest, out EmulatorInstallPlan plan)
        {
            plan = null;
            if (manifest == null ||
                !Guid.TryParse(manifest.EmulatorId, out var emulatorId) ||
                string.IsNullOrWhiteSpace(manifest.EmulatorProfileId) ||
                string.IsNullOrWhiteSpace(manifest.PlatformSpecificationId))
            {
                return false;
            }

            var emulator = database.Emulators.Get(emulatorId);
            var profile = emulator?.GetProfile(manifest.EmulatorProfileId);
            var platform = emulationApi.GetPlatform(manifest.PlatformSpecificationId);
            if (emulator == null || profile == null || platform == null) return false;
            plan = new EmulatorInstallPlan(
                platform.Name,
                platform.Id,
                emulator.Id,
                emulator.Name,
                profile.Id,
                profile.Name,
                NormalizeExtension(Path.GetExtension(manifest.RomTarget)),
                string.Equals(emulator.BuiltInConfigId, "mame", StringComparison.OrdinalIgnoreCase)
                    ? "-rompath \"{ImageDir}\""
                    : null);
            return true;
        }

        private EmulatedPlatform ResolvePlatform(CloudGameClassification classification)
        {
            var platform = !string.IsNullOrWhiteSpace(classification.PlatformSpecificationId)
                ? emulationApi.GetPlatform(classification.PlatformSpecificationId)
                : null;
            platform = platform ?? emulationApi.Platforms.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, classification.PlatformName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Id, classification.PlatformName, StringComparison.OrdinalIgnoreCase));
            if (platform == null)
            {
                throw new InvalidOperationException(
                    $"'{classification.PlatformName}' is not a platform known to Playnite's emulation system. " +
                    "Rename the Cloud Storage platform folder to a Playnite platform name.");
            }

            return platform;
        }

        private void AddBuiltInMatches(
            ICollection<EmulatorInstallPlan> matches,
            Emulator emulator,
            EmulatedPlatform platform,
            IReadOnlyList<string> extensions,
            bool requireDeclaredExtension)
        {
            if (string.IsNullOrWhiteSpace(emulator.BuiltInConfigId)) return;
            var definition = emulationApi.GetEmulator(emulator.BuiltInConfigId);
            if (definition == null) return;
            foreach (var profile in emulator.BuiltinProfiles ?? new System.Collections.ObjectModel.ObservableCollection<BuiltInEmulatorProfile>())
            {
                var profileDefinition = definition.Profiles?.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, profile.BuiltInProfileName, StringComparison.Ordinal));
                var extension = profileDefinition == null
                    ? null
                    : FindSupportedExtension(profileDefinition.ImageExtensions, extensions, requireDeclaredExtension);
                if (profileDefinition == null ||
                    profileDefinition.Platforms?.Contains(platform.Id) != true ||
                    extension == null)
                {
                    continue;
                }

                matches.Add(new EmulatorInstallPlan(
                    platform.Name,
                    platform.Id,
                    emulator.Id,
                    emulator.Name,
                    profile.Id,
                    profile.Name,
                    extension,
                    string.Equals(definition.Id, "mame", StringComparison.OrdinalIgnoreCase)
                        ? "-rompath \"{ImageDir}\""
                        : null));
            }
        }

        private static void AddCustomMatches(
            ICollection<EmulatorInstallPlan> matches,
            Emulator emulator,
            Platform databasePlatform,
            EmulatedPlatform platform,
            IReadOnlyList<string> extensions,
            bool requireDeclaredExtension)
        {
            if (databasePlatform == null) return;
            foreach (var profile in emulator.CustomProfiles ?? new System.Collections.ObjectModel.ObservableCollection<CustomEmulatorProfile>())
            {
                var extension = FindSupportedExtension(profile.ImageExtensions, extensions, requireDeclaredExtension);
                if (profile.Platforms?.Contains(databasePlatform.Id) != true || extension == null)
                {
                    continue;
                }

                matches.Add(new EmulatorInstallPlan(
                    platform.Name,
                    platform.Id,
                    emulator.Id,
                    emulator.Name,
                    profile.Id,
                    profile.Name,
                    extension,
                    null));
            }
        }

        private static string FindSupportedExtension(
            IEnumerable<string> supportedExtensions,
            IReadOnlyList<string> candidates,
            bool requireDeclaredExtension)
        {
            var supported = supportedExtensions?
                .Select(NormalizeExtension)
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .ToList() ?? new List<string>();
            if (supported.Count == 0) return requireDeclaredExtension ? null : candidates.FirstOrDefault();
            return candidates.FirstOrDefault(candidate =>
                supported.Any(extension => string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase)));
        }

        private static IReadOnlyList<string> GetCandidateExtensions(SourcePackage package)
        {
            switch (package.Kind)
            {
                case SourcePackageKind.ScummVmDirectory:
                    return new[] { "scummvm" };
                case SourcePackageKind.MsDosDirectory:
                    return new[] { "jsdos" };
                default:
                    var extension = NormalizeExtension(Path.GetExtension(package.DisplayName));
                    return string.IsNullOrWhiteSpace(extension)
                        ? Array.Empty<string>()
                        : new[] { extension };
            }
        }

        private static string NormalizeExtension(string extension)
        {
            return string.IsNullOrWhiteSpace(extension)
                ? null
                : extension.Trim().TrimStart('.').ToLowerInvariant();
        }
    }
}
