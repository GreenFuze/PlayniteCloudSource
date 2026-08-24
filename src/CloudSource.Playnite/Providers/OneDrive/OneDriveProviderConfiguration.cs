using System;

namespace CloudSource.Playnite.Providers.OneDrive
{
    internal sealed class OneDriveProviderConfiguration
    {
        public bool Enabled { get; }
        public string AccountId { get; }
        public string AccountDisplayName { get; }
        public string FolderId { get; }
        public string FolderDisplayPath { get; }

        public bool HasConcreteFolder =>
            !string.IsNullOrWhiteSpace(FolderId) &&
            !string.Equals(FolderId.Trim(), "root", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(FolderDisplayPath) &&
            !string.Equals(FolderDisplayPath.Trim(), "OneDrive", StringComparison.OrdinalIgnoreCase);

        public OneDriveProviderConfiguration(
            bool enabled,
            string accountId,
            string accountDisplayName,
            string folderId,
            string folderDisplayPath)
        {
            Enabled = enabled;
            AccountId = accountId?.Trim();
            AccountDisplayName = accountDisplayName?.Trim();
            FolderId = folderId?.Trim();
            FolderDisplayPath = folderDisplayPath?.Trim();
        }

        public OneDriveAccountConfiguration CreateAccountConfiguration(string clientId)
        {
            return new OneDriveAccountConfiguration(clientId, AccountId, AccountDisplayName);
        }

        public SourceScanRequest CreateScanRequest()
        {
            return new SourceScanRequest(
                AccountId,
                new[] { new SourceLocation(FolderId, FolderDisplayPath, recursive: true) });
        }
    }
}
