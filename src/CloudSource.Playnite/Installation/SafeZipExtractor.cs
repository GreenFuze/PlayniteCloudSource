using CloudSource.Playnite.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace CloudSource.Playnite.Installation
{
    internal sealed class SafeZipExtractor : IArchiveExtractor
    {
        private readonly ArchiveExtractionPolicy extractionPolicy;
        private readonly ArchiveFileWriter fileWriter;

        public SourcePackageKind Kind => SourcePackageKind.ZipArchive;

        public SafeZipExtractor()
            : this(new ArchiveExtractionPolicy(), new ArchiveFileWriter())
        {
        }

        internal SafeZipExtractor(ArchiveExtractionPolicy extractionPolicy, ArchiveFileWriter fileWriter)
        {
            this.extractionPolicy = extractionPolicy ?? throw new ArgumentNullException(nameof(extractionPolicy));
            this.fileWriter = fileWriter ?? throw new ArgumentNullException(nameof(fileWriter));
        }

        public ExtractionResult Extract(string archivePath, string extractionRoot)
        {
            return Extract(archivePath, extractionRoot, CancellationToken.None);
        }

        public ExtractionResult Extract(string archivePath, string extractionRoot, CancellationToken cancellationToken)
        {
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException("ZIP archive does not exist.", archivePath);
            }

            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var entries = archive.Entries.ToList();
                var plan = extractionPolicy.CreatePlan(
                    extractionRoot,
                    entries.Select((entry, index) => new ArchiveEntryDescriptor(
                        index,
                        entry.FullName,
                        string.IsNullOrEmpty(entry.Name),
                        entry.Length,
                        IsLink(entry),
                        false,
                        false)),
                    "ZIP");

                foreach (var target in plan.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = entries[target.Index];
                    if (target.IsDirectory)
                    {
                        Directory.CreateDirectory(target.Destination);
                        continue;
                    }

                    using (var input = entry.Open())
                    {
                        fileWriter.Write(input, target.Destination, target.Size, cancellationToken);
                    }
                }

                return extractionPolicy.Complete(extractionRoot, plan.ExpandedBytes);
            }
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
