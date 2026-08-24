using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CloudSource.Playnite.Providers
{
    internal sealed class CloudPackageDiscovery
    {
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
            foreach (var file in fileList)
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

            foreach (var setup in fileList.Where(file => IsInstallerSetupName(file.DisplayName)))
            {
                var packageFiles = new List<SourcePackageFile>
                {
                    CreatePackageFile(setup, SourcePackageFileRole.Primary)
                };
                packageFiles.AddRange(fileList
                    .Where(file => IsMatchingInstallerCompanion(setup.DisplayName, file.DisplayName))
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
