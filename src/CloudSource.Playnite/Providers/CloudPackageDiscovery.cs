using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CloudSource.Playnite.Providers
{
    internal sealed class CloudPackageDiscovery
    {
        private static readonly IReadOnlyDictionary<string, SourcePackageKind> DirectoryPlatformKinds =
            new Dictionary<string, SourcePackageKind>(StringComparer.OrdinalIgnoreCase)
            {
                ["scummvm"] = SourcePackageKind.ScummVmDirectory,
                ["ms-dos"] = SourcePackageKind.MsDosDirectory,
                ["ms dos"] = SourcePackageKind.MsDosDirectory,
                ["msdos"] = SourcePackageKind.MsDosDirectory
            };
        private static readonly HashSet<string> RomExtensions = new HashSet<string>(
            new[]
            {
                ".32x", ".a26", ".a52", ".a78", ".col", ".fds", ".gb", ".gba", ".gbc", ".gen",
                ".gg", ".int", ".j64", ".jag", ".lnx", ".md", ".n64", ".nds", ".nes", ".ngc",
                ".ngp", ".pce", ".rom", ".sfc", ".smc", ".smd", ".sms", ".unf", ".unif", ".v64",
                ".vec", ".ws", ".wsc", ".z64"
            },
            StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> IntrinsicPlatformRomExtensions = new HashSet<string>(
            new[] { ".32x", ".fds", ".gb", ".gba", ".gbc", ".gen", ".gg", ".md", ".n64", ".nes", ".sfc", ".smc", ".smd", ".sms", ".v64", ".z64" },
            StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<SourcePackage> Discover(
            string providerId,
            string accountId,
            IEnumerable<CloudFileEntry> files)
        {
            providerId = Required(providerId, nameof(providerId));
            accountId = Required(accountId, nameof(accountId));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var fileList = files.ToList();
            if (fileList.Any(file => file == null))
                throw new ArgumentException("Cloud file collection cannot contain null entries.", nameof(files));

            var packages = new List<SourcePackage>();
            var directoryFiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in GroupDirectoryPackages(fileList))
            {
                packages.Add(CreateDirectoryPackage(providerId, accountId, group));
                foreach (var file in group.Files) directoryFiles.Add(file.ObjectId);
            }

            foreach (var file in fileList.Where(file => !directoryFiles.Contains(file.ObjectId)))
            {
                if (!TryGetPackageKind(file.DisplayName, file.LogicalPath, out var kind)) continue;
                packages.Add(new SourcePackage(
                    providerId,
                    accountId,
                    file.ObjectId,
                    file.Revision,
                    file.LogicalPath,
                    file.DisplayName,
                    file.SizeBytes,
                    file.ModifiedAt,
                    kind));
            }

            foreach (var setup in fileList.Where(file =>
                !directoryFiles.Contains(file.ObjectId) && IsInstallerSetupName(file.DisplayName)))
            {
                var packageFiles = new List<SourcePackageFile>
                {
                    CreatePackageFile(setup, SourcePackageFileRole.Primary)
                };
                packageFiles.AddRange(fileList
                    .Where(file => !directoryFiles.Contains(file.ObjectId) &&
                        IsSameDirectory(setup.LogicalPath, file.LogicalPath) &&
                        IsMatchingInstallerCompanion(setup.DisplayName, file.DisplayName))
                    .OrderBy(file => file.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(file => CreatePackageFile(file, SourcePackageFileRole.Companion)));

                long totalSize;
                try
                {
                    totalSize = packageFiles.Aggregate(0L, (total, file) => checked(total + file.SizeBytes));
                }
                catch (OverflowException exception)
                {
                    throw new InvalidDataException($"Installer package '{setup.LogicalPath}' is too large.", exception);
                }

                packages.Add(new SourcePackage(
                    providerId,
                    accountId,
                    setup.ObjectId,
                    string.Join("|", packageFiles.Select(file => file.Revision)),
                    setup.LogicalPath,
                    setup.DisplayName,
                    totalSize,
                    packageFiles
                        .Select(file => fileList.Single(source => source.ObjectId == file.ObjectId).ModifiedAt)
                        .Where(value => value.HasValue)
                        .OrderByDescending(value => value.Value)
                        .FirstOrDefault(),
                    SourcePackageKind.InnoInstallerBundle,
                    packageFiles));
            }

            return packages;
        }

        private static IReadOnlyList<DirectoryPackageGroup> GroupDirectoryPackages(
            IEnumerable<CloudFileEntry> files)
        {
            var groups = new Dictionary<string, DirectoryPackageGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (!TryGetDirectoryPackage(file.LogicalPath, out var rootPath, out var displayName, out var kind))
                    continue;
                var key = kind + "|" + rootPath;
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new DirectoryPackageGroup(rootPath, displayName, kind);
                    groups.Add(key, group);
                }
                group.Files.Add(file);
            }

            return groups.Values
                .Where(group => group.Files.Count > 0)
                .OrderBy(group => group.RootPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool TryGetDirectoryPackage(
            string logicalPath,
            out string rootPath,
            out string displayName,
            out SourcePackageKind kind)
        {
            var segments = SplitPath(logicalPath);
            if (TryGetDirectoryPlatform(segments, out var platformIndex, out kind) &&
                platformIndex + 2 < segments.Count)
            {
                displayName = segments[platformIndex + 2];
                rootPath = string.Join("/", segments.Take(platformIndex + 3));
                return true;
            }

            rootPath = null;
            displayName = null;
            kind = default(SourcePackageKind);
            return false;
        }

        internal static bool TryGetDirectoryPackageKind(string logicalPath, out SourcePackageKind kind) =>
            TryGetDirectoryPlatform(SplitPath(logicalPath), out _, out kind);

        private static bool TryGetDirectoryPlatform(
            IReadOnlyList<string> segments,
            out int platformIndex,
            out SourcePackageKind kind)
        {
            for (var index = 0; index + 1 < segments.Count; index++)
            {
                if (string.Equals(segments[index], "Platforms", StringComparison.OrdinalIgnoreCase) &&
                    DirectoryPlatformKinds.TryGetValue(segments[index + 1], out kind))
                {
                    platformIndex = index;
                    return true;
                }
            }

            platformIndex = -1;
            kind = default(SourcePackageKind);
            return false;
        }

        private static SourcePackage CreateDirectoryPackage(
            string providerId,
            string accountId,
            DirectoryPackageGroup group)
        {
            var ordered = group.Files
                .OrderBy(file => file.LogicalPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.ObjectId, StringComparer.Ordinal)
                .ToList();
            long totalSize;
            try
            {
                totalSize = ordered.Aggregate(0L, (total, file) => checked(total + file.SizeBytes));
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException($"Directory package '{group.RootPath}' is too large.", exception);
            }

            var files = ordered.Select((file, index) =>
                CreatePackageFile(file, index == 0 ? SourcePackageFileRole.Primary : SourcePackageFileRole.Companion))
                .ToList();
            var modifiedAt = ordered
                .Where(file => file.ModifiedAt.HasValue)
                .Select(file => file.ModifiedAt)
                .OrderByDescending(value => value.Value)
                .FirstOrDefault();
            return new SourcePackage(
                providerId,
                accountId,
                "directory-" + Sha256(group.RootPath.ToLowerInvariant()),
                Sha256(string.Join("\n", ordered.Select(file => file.ObjectId + "\0" + file.Revision))),
                group.RootPath,
                group.DisplayName,
                totalSize,
                modifiedAt,
                group.Kind,
                files);
        }

        private static bool IsSameDirectory(string firstPath, string secondPath) =>
            string.Equals(
                Path.GetDirectoryName((firstPath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar)),
                Path.GetDirectoryName((secondPath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar)),
                StringComparison.OrdinalIgnoreCase);

        private static IReadOnlyList<string> SplitPath(string logicalPath) =>
            (logicalPath ?? string.Empty)
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.Trim())
                .Where(segment => segment.Length > 0)
                .ToList();

        private static string Sha256(string value)
        {
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value)))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private sealed class DirectoryPackageGroup
        {
            public string RootPath { get; }
            public string DisplayName { get; }
            public SourcePackageKind Kind { get; }
            public List<CloudFileEntry> Files { get; } = new List<CloudFileEntry>();

            public DirectoryPackageGroup(string rootPath, string displayName, SourcePackageKind kind)
            {
                RootPath = rootPath;
                DisplayName = displayName;
                Kind = kind;
            }
        }

        private static SourcePackageFile CreatePackageFile(CloudFileEntry file, SourcePackageFileRole role)
        {
            return new SourcePackageFile(
                file.ObjectId,
                file.Revision,
                file.LogicalPath,
                file.DisplayName,
                file.SizeBytes,
                role);
        }

        private static bool IsInstallerSetupName(string name)
        {
            var fileName = Path.GetFileName(name ?? string.Empty);
            return string.Equals(fileName, "setup.exe", StringComparison.OrdinalIgnoreCase) ||
                (fileName.StartsWith("setup_", StringComparison.OrdinalIgnoreCase) &&
                 fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsMatchingInstallerCompanion(string setupName, string candidateName)
        {
            if (!candidateName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) return false;
            var setupStem = Path.GetFileNameWithoutExtension(setupName);
            var companionStem = Path.GetFileNameWithoutExtension(candidateName);
            var separator = companionStem.LastIndexOf('-');
            if (separator <= 0 || separator == companionStem.Length - 1) return false;
            if (!companionStem.Substring(separator + 1).All(char.IsDigit)) return false;
            return string.Equals(companionStem.Substring(0, separator), setupStem, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetPackageKind(string name, string logicalPath, out SourcePackageKind kind)
        {
            var extension = Path.GetExtension(name);
            if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                kind = SourcePackageKind.ZipArchive;
                return true;
            }
            if (string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase))
            {
                kind = SourcePackageKind.SevenZipArchive;
                return true;
            }
            if (string.Equals(extension, ".rar", StringComparison.OrdinalIgnoreCase))
            {
                kind = SourcePackageKind.RarArchive;
                return true;
            }
            if (RomExtensions.Contains(extension) &&
                (IntrinsicPlatformRomExtensions.Contains(extension) || IsPlatformPath(logicalPath)))
            {
                kind = SourcePackageKind.RomFile;
                return true;
            }

            kind = default(SourcePackageKind);
            return false;
        }

        private static bool IsPlatformPath(string logicalPath)
        {
            var segments = (logicalPath ?? string.Empty)
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index + 1 < segments.Length; index++)
            {
                if (string.Equals(segments[index], "Platforms", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }
}
