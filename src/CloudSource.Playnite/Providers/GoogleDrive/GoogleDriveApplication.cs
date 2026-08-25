using System;
using System.Linq;
using System.Reflection;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal static class GoogleDriveApplication
    {
        private const string ClientSecretMetadataKey = "CloudSourceGoogleClientSecret";
        private const string PickerApiKeyMetadataKey = "CloudSourceGooglePickerApiKey";
        public const string ClientId = "741674503892-h3ig2v264cl4813m0135psblvsi47q95.apps.googleusercontent.com";
        public const string ProjectNumber = "741674503892";

        public static GoogleOAuthClientCredentials CreateCredentials()
        {
            var secret = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .SingleOrDefault(attribute =>
                    string.Equals(attribute.Key, ClientSecretMetadataKey, StringComparison.Ordinal))
                ?.Value;
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException(
                    "This Cloud Storage build has no Google desktop OAuth credential configured.");
            }

            return new GoogleOAuthClientCredentials(ClientId, secret);
        }

        public static GoogleDrivePickerConfiguration CreatePickerConfiguration()
        {
            var apiKey = GetMetadata(PickerApiKeyMetadataKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "This Cloud Storage build has no Google Picker API key configured.");
            }

            return new GoogleDrivePickerConfiguration(
                ProjectNumber,
                apiKey);
        }

        private static string GetMetadata(string key)
        {
            return Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .SingleOrDefault(attribute =>
                    string.Equals(attribute.Key, key, StringComparison.Ordinal))
                ?.Value;
        }
    }
}
