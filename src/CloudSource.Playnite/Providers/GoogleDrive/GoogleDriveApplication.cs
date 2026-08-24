using System;
using System.Linq;
using System.Reflection;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal static class GoogleDriveApplication
    {
        private const string ClientSecretMetadataKey = "CloudSourceGoogleClientSecret";
        public const string ClientId = "741674503892-h3ig2v264cl4813m0135psblvsi47q95.apps.googleusercontent.com";

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
    }
}
