using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudSource.Playnite.Providers
{
    public enum SourcePackageKind
    {
        ZipArchive,
        SevenZipArchive,
        RarArchive,
        InnoInstallerBundle,
        RomFile,
        ScummVmDirectory,
        MsDosDirectory
    }

    public enum SourcePackageFileRole
    {
        Primary,
        Companion
    }

    public sealed class SourcePackageFile
    {
        public string ObjectId { get; }
        public string Revision { get; }
        public string LogicalPath { get; }
        public string DisplayName { get; }
        public long SizeBytes { get; }
        public SourcePackageFileRole Role { get; }

        public SourcePackageFile(
            string objectId,
            string revision,
            string logicalPath,
            string displayName,
            long sizeBytes,
            SourcePackageFileRole role)
        {
            ObjectId = Required(objectId, nameof(objectId));
            Revision = Required(revision, nameof(revision));
            LogicalPath = Required(logicalPath, nameof(logicalPath));
            DisplayName = Required(displayName, nameof(displayName));
            if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
            SizeBytes = sizeBytes;
            Role = role;
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }

    public sealed class SourcePackage
    {
        public string ProviderId { get; }
        public string AccountId { get; }
        public string ObjectId { get; }
        public string Revision { get; }
        public string LogicalPath { get; }
        public string DisplayName { get; }
        public long SizeBytes { get; }
        public DateTimeOffset? ModifiedAt { get; }
        public SourcePackageKind Kind { get; }
        public IReadOnlyList<SourcePackageFile> Files { get; }

        public string StableId => $"{ProviderId}:{AccountId}:{ObjectId}";

        public SourcePackage(
            string providerId,
            string accountId,
            string objectId,
            string revision,
            string logicalPath,
            string displayName,
            long sizeBytes,
            DateTimeOffset? modifiedAt,
            SourcePackageKind kind,
            IEnumerable<SourcePackageFile> files = null)
        {
            ProviderId = Required(providerId, nameof(providerId));
            AccountId = Required(accountId, nameof(accountId));
            ObjectId = Required(objectId, nameof(objectId));
            Revision = Required(revision, nameof(revision));
            LogicalPath = Required(logicalPath, nameof(logicalPath));
            DisplayName = Required(displayName, nameof(displayName));
            if (sizeBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Package size cannot be negative.");
            }

            SizeBytes = sizeBytes;
            ModifiedAt = modifiedAt;
            Kind = kind;
            var packageFiles = files?.ToList() ?? new List<SourcePackageFile>
            {
                new SourcePackageFile(
                    ObjectId,
                    Revision,
                    LogicalPath,
                    DisplayName,
                    SizeBytes,
                    SourcePackageFileRole.Primary)
            };
            if (packageFiles.Count == 0 || packageFiles.Any(file => file == null))
            {
                throw new ArgumentException("A source package must contain files.", nameof(files));
            }

            if (packageFiles.Count(file => file.Role == SourcePackageFileRole.Primary) != 1)
            {
                throw new ArgumentException("A source package must identify exactly one primary file.", nameof(files));
            }

            if (!IsDirectoryPackage(kind) &&
                !packageFiles.Any(file => file.Role == SourcePackageFileRole.Primary &&
                    string.Equals(file.ObjectId, ObjectId, StringComparison.Ordinal)))
            {
                throw new ArgumentException("A file package primary file must match the package object ID.", nameof(files));
            }

            Files = packageFiles.AsReadOnly();
        }

        public static bool IsDirectoryPackage(SourcePackageKind kind) =>
            kind == SourcePackageKind.ScummVmDirectory || kind == SourcePackageKind.MsDosDirectory;

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value is required.", parameterName);
            }

            return value.Trim();
        }
    }
}
