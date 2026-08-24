using System;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal sealed class GoogleDriveProviderConfiguration
    {
        public bool Enabled { get; }
        public string ClientId { get; }
        public string ClientSecret { get; }
        public string AccountId { get; }
        public string AccountDisplayName { get; }
        public string FolderId { get; }
        public string FolderDisplayPath { get; }

        public bool HasConcreteFolder =>
            !string.IsNullOrWhiteSpace(FolderId) &&
            !string.Equals(FolderId.Trim(), "root", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(FolderDisplayPath) &&
            !string.Equals(FolderDisplayPath.Trim(), "My Drive", StringComparison.OrdinalIgnoreCase);

        public GoogleDriveProviderConfiguration(
            bool enabled,
            string clientId,
            string clientSecret,
            string accountId,
            string accountDisplayName,
            string folderId,
            string folderDisplayPath)
        {
            Enabled = enabled;
            ClientId = clientId?.Trim();
            ClientSecret = clientSecret?.Trim();
            AccountId = accountId?.Trim();
            AccountDisplayName = accountDisplayName?.Trim();
            FolderId = folderId?.Trim();
            FolderDisplayPath = folderDisplayPath?.Trim();
        }

        public GoogleDriveAccountConfiguration CreateAccountConfiguration()
        {
            return new GoogleDriveAccountConfiguration(ClientId, ClientSecret, AccountId, AccountDisplayName);
        }

        public SourceScanRequest CreateScanRequest()
        {
            return new SourceScanRequest(
                AccountId,
                new[] { new SourceLocation(FolderId, FolderDisplayPath, recursive: true) });
        }
    }
}
