using CloudSource.Playnite.Providers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudSource.Playnite.GameImport
{
    internal enum CloudContentKind
    {
        NativePackage,
        Rom
    }

    internal sealed class CloudGameClassification
    {
        public CloudContentKind ContentKind { get; }
        public string PlatformName { get; }
        public string PlatformSpecificationId { get; }

        public CloudGameClassification(
            CloudContentKind contentKind,
            string platformName,
            string platformSpecificationId)
        {
            ContentKind = contentKind;
            PlatformName = platformName;
            PlatformSpecificationId = platformSpecificationId;
        }
    }

    internal sealed class CloudArchiveClassifier
    {
        private static readonly HashSet<string> NonGameFolders = new HashSet<string>(
            new[] { "mga_save_sync", "mga_sync" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlyDictionary<string, PlatformIdentity> PlatformAliases =
            new Dictionary<string, PlatformIdentity>(StringComparer.OrdinalIgnoreCase)
            {
                ["arcade"] = new PlatformIdentity("Arcade", "arcade"),
                ["mame"] = new PlatformIdentity("Arcade", "arcade"),
                ["nes"] = new PlatformIdentity("Nintendo Entertainment System", "nintendo_nes"),
                ["nintendo entertainment system"] = new PlatformIdentity("Nintendo Entertainment System", "nintendo_nes"),
                ["snes"] = new PlatformIdentity("Nintendo SNES", "nintendo_super_nes"),
                ["super nintendo"] = new PlatformIdentity("Nintendo SNES", "nintendo_super_nes"),
                ["nintendo snes"] = new PlatformIdentity("Nintendo SNES", "nintendo_super_nes"),
                ["gb"] = new PlatformIdentity("Nintendo Game Boy", "nintendo_gameboy"),
                ["game boy"] = new PlatformIdentity("Nintendo Game Boy", "nintendo_gameboy"),
                ["gbc"] = new PlatformIdentity("Nintendo Game Boy Color", "nintendo_gameboycolor"),
                ["game boy color"] = new PlatformIdentity("Nintendo Game Boy Color", "nintendo_gameboycolor"),
                ["gba"] = new PlatformIdentity("Nintendo Game Boy Advance", "nintendo_gameboyadvance"),
                ["nintendo game boy advance"] = new PlatformIdentity("Nintendo Game Boy Advance", "nintendo_gameboyadvance"),
                ["n64"] = new PlatformIdentity("Nintendo 64", "nintendo_64"),
                ["nintendo 64"] = new PlatformIdentity("Nintendo 64", "nintendo_64"),
                ["genesis"] = new PlatformIdentity("Sega Genesis", "sega_genesis"),
                ["megadrive"] = new PlatformIdentity("Sega Genesis", "sega_genesis"),
                ["mega drive"] = new PlatformIdentity("Sega Genesis", "sega_genesis"),
                ["sega genesis"] = new PlatformIdentity("Sega Genesis", "sega_genesis"),
                ["mastersystem"] = new PlatformIdentity("Sega Master System", "sega_mastersystem"),
                ["master system"] = new PlatformIdentity("Sega Master System", "sega_mastersystem"),
                ["sms"] = new PlatformIdentity("Sega Master System", "sega_mastersystem"),
                ["gamegear"] = new PlatformIdentity("Sega Game Gear", "sega_gamegear"),
                ["game gear"] = new PlatformIdentity("Sega Game Gear", "sega_gamegear"),
                ["sega32x"] = new PlatformIdentity("Sega 32X", "sega_32x"),
                ["32x"] = new PlatformIdentity("Sega 32X", "sega_32x"),
                ["scummvm"] = new PlatformIdentity("PC (DOS)", "pc_dos"),
                ["ms-dos"] = new PlatformIdentity("PC (DOS)", "pc_dos"),
                ["ms dos"] = new PlatformIdentity("PC (DOS)", "pc_dos"),
                ["msdos"] = new PlatformIdentity("PC (DOS)", "pc_dos"),
                ["windows"] = new PlatformIdentity("PC (Windows)", "pc_windows"),
                ["windows pc"] = new PlatformIdentity("PC (Windows)", "pc_windows"),
                ["pc (windows)"] = new PlatformIdentity("PC (Windows)", "pc_windows")
            };

        public bool ShouldImport(SourcePackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            return !GetSegments(package.LogicalPath).Any(NonGameFolders.Contains);
        }

        public string ResolvePlatform(string logicalPath)
        {
            return Classify(logicalPath).PlatformName;
        }

        public CloudGameClassification Classify(SourcePackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            return Classify(package.LogicalPath);
        }

        public CloudGameClassification Classify(string logicalPath)
        {
            var segments = GetSegments(logicalPath);
            if (segments.Any(segment => string.Equals(segment, "Installers", StringComparison.OrdinalIgnoreCase)))
                return new CloudGameClassification(CloudContentKind.NativePackage, "PC (Windows)", "pc_windows");

            for (var index = 0; index < segments.Count; index++)
            {
                if (string.Equals(segments[index], "Platforms", StringComparison.OrdinalIgnoreCase) &&
                    index + 1 < segments.Count)
                {
                    var platform = CanonicalPlatform(segments[index + 1]);
                    var contentKind = string.Equals(platform.SpecificationId, "pc_windows", StringComparison.Ordinal)
                        ? CloudContentKind.NativePackage
                        : CloudContentKind.Rom;
                    return new CloudGameClassification(contentKind, platform.Name, platform.SpecificationId);
                }

            }

            foreach (var segment in segments.Take(Math.Max(0, segments.Count - 1)))
            {
                if (!PlatformAliases.TryGetValue(segment, out var platform)) continue;
                var contentKind = string.Equals(platform.SpecificationId, "pc_windows", StringComparison.Ordinal)
                    ? CloudContentKind.NativePackage
                    : CloudContentKind.Rom;
                return new CloudGameClassification(contentKind, platform.Name, platform.SpecificationId);
            }

            var inferred = InferPlatformFromExtension(System.IO.Path.GetExtension(segments.Last()));
            if (inferred != null)
                return new CloudGameClassification(CloudContentKind.Rom, inferred.Name, inferred.SpecificationId);

            return new CloudGameClassification(CloudContentKind.NativePackage, null, null);
        }

        public string GetRawTitleFromLogicalPath(string logicalPath)
        {
            var segments = GetSegments(logicalPath);
            if (segments.Count == 0)
            {
                throw new ArgumentException("A cloud archive path must contain a filename.", nameof(logicalPath));
            }

            var filename = segments[segments.Count - 1];
            var extensionIndex = filename.LastIndexOf('.');
            return extensionIndex > 0 ? filename.Substring(0, extensionIndex) : filename;
        }

        private static PlatformIdentity CanonicalPlatform(string value)
        {
            var candidate = value?.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return new PlatformIdentity(null, null);
            }

            return PlatformAliases.TryGetValue(candidate, out var canonical)
                ? canonical
                : new PlatformIdentity(candidate, null);
        }

        private static IReadOnlyList<string> GetSegments(string logicalPath)
        {
            if (string.IsNullOrWhiteSpace(logicalPath))
            {
                throw new ArgumentException("A cloud archive path is required.", nameof(logicalPath));
            }

            return logicalPath
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.Trim())
                .Where(segment => segment.Length > 0)
                .ToList();
        }

        private static PlatformIdentity InferPlatformFromExtension(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".nes":
                case ".fds": return PlatformAliases["nes"];
                case ".smc":
                case ".sfc": return PlatformAliases["snes"];
                case ".gb": return PlatformAliases["gb"];
                case ".gbc": return PlatformAliases["gbc"];
                case ".gba": return PlatformAliases["gba"];
                case ".z64":
                case ".n64":
                case ".v64": return PlatformAliases["n64"];
                case ".gen":
                case ".md":
                case ".smd": return PlatformAliases["genesis"];
                case ".sms": return PlatformAliases["sms"];
                case ".gg": return PlatformAliases["gamegear"];
                case ".32x": return PlatformAliases["32x"];
                default: return null;
            }
        }

        private sealed class PlatformIdentity
        {
            public string Name { get; }
            public string SpecificationId { get; }

            public PlatformIdentity(string name, string specificationId)
            {
                Name = name;
                SpecificationId = specificationId;
            }
        }
    }
}
