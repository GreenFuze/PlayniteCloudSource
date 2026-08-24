using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CloudSource.Playnite.Providers.OneDrive
{
    internal sealed class ProtectedOneDriveTokenStore : IOneDriveTokenStore
    {
        private readonly string tokenPath;
        private readonly byte[] entropy;

        public bool Exists => File.Exists(tokenPath);

        public ProtectedOneDriveTokenStore(string tokenPath, Guid pluginId)
        {
            if (string.IsNullOrWhiteSpace(tokenPath))
                throw new ArgumentException("Token path is required.", nameof(tokenPath));
            this.tokenPath = Path.GetFullPath(tokenPath);
            entropy = Encoding.UTF8.GetBytes(pluginId.ToString("N") + ":onedrive");
        }

        public OneDriveToken Load()
        {
            if (!Exists) throw new InvalidOperationException("OneDrive is not connected.");
            var clearBytes = ProtectedData.Unprotect(
                File.ReadAllBytes(tokenPath),
                entropy,
                DataProtectionScope.CurrentUser);
            var token = OneDriveJson.Deserialize<OneDriveToken>(clearBytes);
            if (string.IsNullOrWhiteSpace(token.AccessToken) && string.IsNullOrWhiteSpace(token.RefreshToken))
                throw new InvalidDataException("The stored OneDrive authorization is empty.");
            return token;
        }

        public void Save(OneDriveToken token)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));
            Directory.CreateDirectory(Path.GetDirectoryName(tokenPath));
            var protectedBytes = ProtectedData.Protect(
                OneDriveJson.Serialize(token),
                entropy,
                DataProtectionScope.CurrentUser);
            var stagingPath = tokenPath + ".staging";
            var backupPath = tokenPath + ".backup";
            File.WriteAllBytes(stagingPath, protectedBytes);
            if (File.Exists(tokenPath))
            {
                File.Replace(stagingPath, tokenPath, backupPath, ignoreMetadataErrors: true);
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
            else
            {
                File.Move(stagingPath, tokenPath);
            }
        }

        public void Clear()
        {
            DeleteIfExists(tokenPath);
            DeleteIfExists(tokenPath + ".staging");
            DeleteIfExists(tokenPath + ".backup");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
