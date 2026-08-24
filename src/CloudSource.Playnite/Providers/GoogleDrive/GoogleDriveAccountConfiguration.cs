using System;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    public sealed class GoogleDriveAccountConfiguration
    {
        public string ClientId { get; }
        public string AccountId { get; }
        public string AccountDisplayName { get; }

        public GoogleDriveAccountConfiguration(
            string clientId,
            string accountId,
            string accountDisplayName)
        {
            ClientId = Required(clientId, nameof(clientId));
            AccountId = Required(accountId, nameof(accountId));
            AccountDisplayName = Required(accountDisplayName, nameof(accountDisplayName));
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
