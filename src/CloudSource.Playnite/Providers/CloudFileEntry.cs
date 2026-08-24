using System;

namespace CloudSource.Playnite.Providers
{
    internal sealed class CloudFileEntry
    {
        public string ObjectId { get; }
        public string Revision { get; }
        public string LogicalPath { get; }
        public string DisplayName { get; }
        public long SizeBytes { get; }
        public DateTimeOffset? ModifiedAt { get; }

        public CloudFileEntry(
            string objectId,
            string revision,
            string logicalPath,
            string displayName,
            long sizeBytes,
            DateTimeOffset? modifiedAt)
        {
            ObjectId = Required(objectId, nameof(objectId));
            Revision = Required(revision, nameof(revision));
            LogicalPath = Required(logicalPath, nameof(logicalPath));
            DisplayName = Required(displayName, nameof(displayName));
            if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
            SizeBytes = sizeBytes;
            ModifiedAt = modifiedAt;
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }
}
