using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal sealed class ProtectedGoogleDriveTokenStore : IGoogleDriveTokenStore
    {
        private readonly string tokenPath;
        private readonly byte[] entropy;

        public bool Exists => File.Exists(tokenPath);

        public ProtectedGoogleDriveTokenStore(string tokenPath, Guid pluginId)
        {
            if (string.IsNullOrWhiteSpace(tokenPath))
            {
                throw new ArgumentException("Token path is required.", nameof(tokenPath));
            }

            this.tokenPath = Path.GetFullPath(tokenPath);
            entropy = Encoding.UTF8.GetBytes(pluginId.ToString("N"));
        }

        public GoogleDriveToken Load()
        {
            if (!Exists)
            {
                throw new InvalidOperationException("Google Drive is not connected.");
            }

            var protectedBytes = File.ReadAllBytes(tokenPath);
            var clearBytes = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
            var token = GoogleDriveJson.Deserialize<GoogleDriveToken>(clearBytes);
            if (string.IsNullOrWhiteSpace(token.AccessToken) && string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                throw new InvalidDataException("The stored Google Drive authorization is empty.");
            }

            return token;
        }

        public void Save(GoogleDriveToken token)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            var directory = Path.GetDirectoryName(tokenPath);
            Directory.CreateDirectory(directory);
            var clearBytes = GoogleDriveJson.Serialize(token);
            var protectedBytes = ProtectedData.Protect(clearBytes, entropy, DataProtectionScope.CurrentUser);
            var stagingPath = tokenPath + ".staging";
            var backupPath = tokenPath + ".backup";

            File.WriteAllBytes(stagingPath, protectedBytes);
            if (File.Exists(tokenPath))
            {
                File.Replace(stagingPath, tokenPath, backupPath, ignoreMetadataErrors: true);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            else
            {
                File.Move(stagingPath, tokenPath);
            }
        }

        public void Clear()
        {
            if (File.Exists(tokenPath))
            {
                File.Delete(tokenPath);
            }
        }
    }
}
