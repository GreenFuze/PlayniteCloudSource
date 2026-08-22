using System;

namespace CloudSource.Playnite.Providers
{
    public enum SourcePackageKind
    {
        ZipArchive,
        SevenZipArchive,
        RarArchive
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
            SourcePackageKind kind)
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
        }

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
