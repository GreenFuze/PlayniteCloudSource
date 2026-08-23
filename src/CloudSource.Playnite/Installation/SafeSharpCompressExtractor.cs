using CloudSource.Playnite.Providers;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CloudSource.Playnite.Installation
{
    internal sealed class SafeSharpCompressExtractor : IArchiveExtractor
    {
        private readonly ArchiveType expectedType;
        private readonly string archiveLabel;
        private readonly ArchiveExtractionPolicy extractionPolicy;
        private readonly ArchiveFileWriter fileWriter;

        public SourcePackageKind Kind { get; }

        public SafeSharpCompressExtractor(SourcePackageKind kind)
            : this(kind, new ArchiveExtractionPolicy(), new ArchiveFileWriter())
        {
        }

        internal SafeSharpCompressExtractor(
            SourcePackageKind kind,
            ArchiveExtractionPolicy extractionPolicy,
            ArchiveFileWriter fileWriter)
        {
            switch (kind)
            {
                case SourcePackageKind.SevenZipArchive:
                    expectedType = ArchiveType.SevenZip;
                    archiveLabel = "7z";
                    break;
                case SourcePackageKind.RarArchive:
                    expectedType = ArchiveType.Rar;
                    archiveLabel = "RAR";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "SharpCompress is only used for 7z and RAR packages.");
            }

            Kind = kind;
            this.extractionPolicy = extractionPolicy ?? throw new ArgumentNullException(nameof(extractionPolicy));
            this.fileWriter = fileWriter ?? throw new ArgumentNullException(nameof(fileWriter));
        }

        public ExtractionResult Extract(string archivePath, string extractionRoot, CancellationToken cancellationToken)
        {
            return Extract(archivePath, extractionRoot, null, cancellationToken);
        }

        public ExtractionResult Extract(
            string archivePath,
            string extractionRoot,
            Action<long, long> reportProgress,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException($"{archiveLabel} archive does not exist.", archivePath);
            }

            using (var archive = ArchiveFactory.Open(archivePath))
            {
                if (archive.Type != expectedType)
                {
                    throw new InvalidDataException(
                        $"Package content is {archive.Type}, but Cloud Storage expected {archiveLabel}.");
                }

                if (!archive.IsComplete || archive.Volumes.Count() != 1)
                {
                    throw new InvalidDataException($"Multi-volume or incomplete {archiveLabel} archives are not supported.");
                }

                var entries = archive.Entries.ToList();
                var plan = extractionPolicy.CreatePlan(
                    extractionRoot,
                    entries.Select(CreateDescriptor),
                    archiveLabel);

                long extractedBytes = 0;
                reportProgress?.Invoke(0, plan.ExpandedBytes);

                foreach (var target in plan.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (target.IsDirectory)
                    {
                        Directory.CreateDirectory(target.Destination);
                        continue;
                    }

                    using (var input = entries[target.Index].OpenEntryStream())
                    {
                        var completedBeforeEntry = extractedBytes;
                        fileWriter.Write(
                            input,
                            target.Destination,
                            target.Size,
                            completedInEntry => reportProgress?.Invoke(
                                completedBeforeEntry + completedInEntry,
                                plan.ExpandedBytes),
                            cancellationToken);
                    }

                    extractedBytes += target.Size;
                }

                return extractionPolicy.Complete(extractionRoot, plan.ExpandedBytes);
            }
        }

        private static ArchiveEntryDescriptor CreateDescriptor(IArchiveEntry entry, int index)
        {
            int? attributes;
            try
            {
                attributes = entry.Attrib;
            }
            catch (NotImplementedException)
            {
                attributes = null;
            }

            var unixType = attributes.HasValue ? (attributes.Value >> 16) & 0xF000 : 0;
            var directUnixType = attributes.HasValue ? attributes.Value & 0xF000 : 0;
            var windowsAttributes = attributes.GetValueOrDefault() & 0xFFFF;
            var isLink = !string.IsNullOrEmpty(entry.LinkTarget) ||
                         unixType == 0xA000 || directUnixType == 0xA000 ||
                         (windowsAttributes & (int)FileAttributes.ReparsePoint) != 0;
            var hasUnsupportedType = IsUnsupportedUnixType(unixType) ||
                                     IsUnsupportedUnixType(directUnixType);
            return new ArchiveEntryDescriptor(
                index,
                entry.Key,
                entry.IsDirectory,
                entry.Size,
                isLink,
                entry.IsEncrypted,
                entry.IsSplitAfter,
                hasUnsupportedType);
        }

        private static bool IsUnsupportedUnixType(int unixType)
        {
            return unixType != 0 && unixType != 0x4000 &&
                   unixType != 0x8000 && unixType != 0xA000;
        }
    }
}
