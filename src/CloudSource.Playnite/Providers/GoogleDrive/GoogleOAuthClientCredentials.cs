using System;
using System.Collections.Generic;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal sealed class GoogleOAuthClientCredentials
    {
        public string ClientId { get; }
        public string ClientSecret { get; }

        public GoogleOAuthClientCredentials(string clientId, string clientSecret)
        {
            ClientId = Required(clientId, nameof(clientId));
            ClientSecret = Required(clientSecret, nameof(clientSecret));
        }

        public void AddTo(IDictionary<string, string> fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            fields["client_id"] = ClientId;
            fields["client_secret"] = ClientSecret;
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }
}
