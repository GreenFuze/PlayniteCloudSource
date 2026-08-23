using CloudSource.Playnite.Providers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudSource.Playnite.GameImport
{
    internal sealed class CloudArchiveClassifier
    {
        private static readonly HashSet<string> NonGameFolders = new HashSet<string>(
            new[] { "mga_save_sync", "mga_sync" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlyDictionary<string, string> PlatformAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["arcade"] = "Arcade",
                ["gba"] = "Nintendo Game Boy Advance",
                ["nintendo game boy advance"] = "Nintendo Game Boy Advance",
                ["windows"] = "PC (Windows)",
                ["windows pc"] = "PC (Windows)",
                ["pc (windows)"] = "PC (Windows)"
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
            var segments = GetSegments(logicalPath);
            for (var index = 0; index < segments.Count; index++)
            {
                if (string.Equals(segments[index], "Platforms", StringComparison.OrdinalIgnoreCase) &&
                    index + 1 < segments.Count)
                {
                    return CanonicalPlatformName(segments[index + 1]);
                }

                if (string.Equals(segments[index], "Installers", StringComparison.OrdinalIgnoreCase))
                {
                    return "PC (Windows)";
                }
            }

            return null;
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

        private static string CanonicalPlatformName(string value)
        {
            var candidate = value?.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            return PlatformAliases.TryGetValue(candidate, out var canonical)
                ? canonical
                : candidate;
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
    }
}
