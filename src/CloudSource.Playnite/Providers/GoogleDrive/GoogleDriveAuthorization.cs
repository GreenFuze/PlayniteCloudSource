using System;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    public sealed class GoogleDriveAuthorization
    {
        internal GoogleDriveToken Token { get; }
        public string AccountId { get; }
        public string AccountDisplayName { get; }

        internal GoogleDriveAuthorization(
            GoogleDriveToken token,
            string accountId,
            string accountDisplayName)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
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
