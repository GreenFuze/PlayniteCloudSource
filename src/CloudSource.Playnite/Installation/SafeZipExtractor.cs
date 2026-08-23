using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CloudSource.Playnite.Installation
{
    internal sealed class SafeZipExtractor
    {
        private const int MaximumEntries = 100000;
        private const long MaximumExpandedBytes = 100L * 1024 * 1024 * 1024;

        public ExtractionResult Extract(string archivePath, string extractionRoot)
        {
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException("ZIP archive does not exist.", archivePath);
            }

            Directory.CreateDirectory(extractionRoot);
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var entries = archive.Entries.ToList();
                if (entries.Count == 0)
                {
                    throw new InvalidDataException("ZIP archive is empty.");
                }

                if (entries.Count > MaximumEntries)
                {
                    throw new InvalidDataException($"ZIP archive has too many entries ({entries.Count}).");
                }

                var expandedBytes = CalculateExpandedSize(entries);
                if (expandedBytes > MaximumExpandedBytes)
                {
                    throw new InvalidDataException("ZIP archive expands beyond the 100 GB safety limit.");
                }

                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(extractionRoot)));
                if (expandedBytes > drive.AvailableFreeSpace)
                {
                    throw new IOException("There is not enough free disk space to extract this game.");
                }

                var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                {
                    var destination = GetValidatedDestination(extractionRoot, entry);
                    if (!destinations.Add(destination))
                    {
                        throw new InvalidDataException($"ZIP archive contains a duplicate path: {entry.FullName}");
                    }
                }

                foreach (var entry in entries)
                {
                    var destination = GetValidatedDestination(extractionRoot, entry);
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    using (var input = entry.Open())
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                }

                return new ExtractionResult(SelectPayloadRoot(extractionRoot), expandedBytes);
            }
        }

        private static long CalculateExpandedSize(IEnumerable<ZipArchiveEntry> entries)
        {
            long total = 0;
            foreach (var entry in entries)
            {
                checked { total += entry.Length; }
            }

            return total;
        }

        private static string GetValidatedDestination(string extractionRoot, ZipArchiveEntry entry)
        {
            if (IsLink(entry))
            {
                throw new InvalidDataException($"ZIP links are not allowed: {entry.FullName}");
            }

            var archivePath = (entry.FullName ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
            if (archivePath.Length == 0 || Path.IsPathRooted(archivePath) || archivePath.IndexOf(':') >= 0 ||
                archivePath.Split(Path.DirectorySeparatorChar).Any(segment => segment == "." || segment == ".."))
            {
                throw new InvalidDataException($"ZIP entry has an unsafe path: {entry.FullName}");
            }

            var root = Path.GetFullPath(extractionRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var destination = Path.GetFullPath(Path.Combine(extractionRoot, archivePath));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"ZIP entry escapes the extraction directory: {entry.FullName}");
            }

            return destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsLink(ZipArchiveEntry entry)
        {
            // ExternalAttributes was added after the net462 reference surface,
            // while Playnite runs on a newer .NET Framework runtime. Reflection
            // keeps the plugin compatible with Playnite's target SDK.
            var property = typeof(ZipArchiveEntry).GetProperty("ExternalAttributes");
            if (property == null)
            {
                return false;
            }

            var attributes = (int)property.GetValue(entry);
            var unixType = (attributes >> 16) & 0xF000;
            var windowsAttributes = attributes & 0xFFFF;
            return unixType == 0xA000 || (windowsAttributes & (int)FileAttributes.ReparsePoint) != 0;
        }

        private static string SelectPayloadRoot(string extractionRoot)
        {
            var rootFiles = Directory.GetFiles(extractionRoot, "*", SearchOption.TopDirectoryOnly);
            var rootDirectories = Directory.GetDirectories(extractionRoot, "*", SearchOption.TopDirectoryOnly);
            return rootFiles.Length == 0 && rootDirectories.Length == 1 ? rootDirectories[0] : extractionRoot;
        }
    }

    internal sealed class ExtractionResult
    {
        public string PayloadRoot { get; }
        public long ExpandedBytes { get; }

        public ExtractionResult(string payloadRoot, long expandedBytes)
        {
            PayloadRoot = payloadRoot;
            ExpandedBytes = expandedBytes;
        }
    }
}
