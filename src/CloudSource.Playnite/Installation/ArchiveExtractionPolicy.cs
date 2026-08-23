using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CloudSource.Playnite.Installation
{
    internal sealed class ArchiveExtractionPolicy
    {
        private const int MaximumEntries = 100000;
        private const long MaximumExpandedBytes = 100L * 1024 * 1024 * 1024;

        public ArchiveExtractionPlan CreatePlan(
            string extractionRoot,
            IEnumerable<ArchiveEntryDescriptor> sourceEntries,
            string archiveLabel)
        {
            if (string.IsNullOrWhiteSpace(extractionRoot))
            {
                throw new ArgumentException("An extraction directory is required.", nameof(extractionRoot));
            }

            if (sourceEntries == null)
            {
                throw new ArgumentNullException(nameof(sourceEntries));
            }

            if (string.IsNullOrWhiteSpace(archiveLabel))
            {
                throw new ArgumentException("An archive label is required.", nameof(archiveLabel));
            }

            var entries = sourceEntries.ToList();
            if (entries.Count == 0)
            {
                throw new InvalidDataException($"{archiveLabel} archive is empty.");
            }

            if (entries.Count > MaximumEntries)
            {
                throw new InvalidDataException($"{archiveLabel} archive has too many entries ({entries.Count}).");
            }

            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targets = new List<PlannedArchiveEntry>(entries.Count);
            long expandedBytes = 0;
            foreach (var entry in entries)
            {
                ValidateEntryType(entry, archiveLabel);
                var destination = GetValidatedDestination(extractionRoot, entry.Key, archiveLabel);
                if (!destinations.Add(destination))
                {
                    throw new InvalidDataException($"{archiveLabel} archive contains a duplicate path: {entry.Key}");
                }

                if (!entry.IsDirectory)
                {
                    try
                    {
                        checked { expandedBytes += entry.Size; }
                    }
                    catch (OverflowException exception)
                    {
                        throw new InvalidDataException($"{archiveLabel} archive expanded size overflowed.", exception);
                    }
                }

                if (expandedBytes > MaximumExpandedBytes)
                {
                    throw new InvalidDataException($"{archiveLabel} archive expands beyond the 100 GB safety limit.");
                }

                targets.Add(new PlannedArchiveEntry(
                    entry.Index,
                    destination,
                    entry.IsDirectory,
                    entry.Size));
            }

            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(extractionRoot)));
            if (expandedBytes > drive.AvailableFreeSpace)
            {
                throw new IOException("There is not enough free disk space to extract this game.");
            }

            Directory.CreateDirectory(extractionRoot);
            return new ArchiveExtractionPlan(targets, expandedBytes);
        }

        public ExtractionResult Complete(string extractionRoot, long expandedBytes)
        {
            var rootFiles = Directory.GetFiles(extractionRoot, "*", SearchOption.TopDirectoryOnly);
            var rootDirectories = Directory.GetDirectories(extractionRoot, "*", SearchOption.TopDirectoryOnly);
            var payloadRoot = rootFiles.Length == 0 && rootDirectories.Length == 1
                ? rootDirectories[0]
                : extractionRoot;
            return new ExtractionResult(payloadRoot, expandedBytes);
        }

        private static void ValidateEntryType(ArchiveEntryDescriptor entry, string archiveLabel)
        {
            if (entry == null)
            {
                throw new InvalidDataException($"{archiveLabel} archive contains an invalid entry.");
            }

            if (entry.IsEncrypted)
            {
                throw new InvalidDataException($"{archiveLabel} entry is encrypted; password-protected archives are not supported: {entry.Key}");
            }

            if (entry.IsLink)
            {
                throw new InvalidDataException($"{archiveLabel} links are not allowed: {entry.Key}");
            }

            if (entry.HasUnsupportedType)
            {
                throw new InvalidDataException($"{archiveLabel} contains an unsupported non-regular entry: {entry.Key}");
            }

            if (entry.IsSplit)
            {
                throw new InvalidDataException($"Multi-volume {archiveLabel} archives are not supported: {entry.Key}");
            }

            if (entry.Size < 0)
            {
                throw new InvalidDataException($"{archiveLabel} entry has an invalid expanded size: {entry.Key}");
            }
        }

        private static string GetValidatedDestination(string extractionRoot, string key, string archiveLabel)
        {
            var archivePath = (key ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (archivePath.Length == 0 || Path.IsPathRooted(archivePath) || archivePath.IndexOf(':') >= 0 ||
                archivePath.Split(Path.DirectorySeparatorChar).Any(segment => segment == "." || segment == ".."))
            {
                throw new InvalidDataException($"{archiveLabel} entry has an unsafe path: {key}");
            }

            var root = Path.GetFullPath(extractionRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var destination = Path.GetFullPath(Path.Combine(extractionRoot, archivePath))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{archiveLabel} entry escapes the extraction directory: {key}");
            }

            return destination;
        }
    }

    internal sealed class ArchiveEntryDescriptor
    {
        public int Index { get; }
        public string Key { get; }
        public bool IsDirectory { get; }
        public long Size { get; }
        public bool IsLink { get; }
        public bool IsEncrypted { get; }
        public bool IsSplit { get; }
        public bool HasUnsupportedType { get; }

        public ArchiveEntryDescriptor(
            int index,
            string key,
            bool isDirectory,
            long size,
            bool isLink,
            bool isEncrypted,
            bool isSplit,
            bool hasUnsupportedType = false)
        {
            Index = index;
            Key = key;
            IsDirectory = isDirectory;
            Size = size;
            IsLink = isLink;
            IsEncrypted = isEncrypted;
            IsSplit = isSplit;
            HasUnsupportedType = hasUnsupportedType;
        }
    }

    internal sealed class PlannedArchiveEntry
    {
        public int Index { get; }
        public string Destination { get; }
        public bool IsDirectory { get; }
        public long Size { get; }

        public PlannedArchiveEntry(int index, string destination, bool isDirectory, long size)
        {
            Index = index;
            Destination = destination;
            IsDirectory = isDirectory;
            Size = size;
        }
    }

    internal sealed class ArchiveExtractionPlan
    {
        public IReadOnlyList<PlannedArchiveEntry> Entries { get; }
        public long ExpandedBytes { get; }

        public ArchiveExtractionPlan(IReadOnlyList<PlannedArchiveEntry> entries, long expandedBytes)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
            ExpandedBytes = expandedBytes;
        }
    }
}
