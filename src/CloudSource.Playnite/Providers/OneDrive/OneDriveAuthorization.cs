using System;

namespace CloudSource.Playnite.Providers.OneDrive
{
    internal sealed class OneDriveAuthorization
    {
        public OneDriveToken Token { get; private set; }
        public string AccountId { get; }
        public string AccountDisplayName { get; }

        public OneDriveAuthorization(OneDriveToken token, string accountId, string accountDisplayName)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
            AccountId = Required(accountId, nameof(accountId));
            AccountDisplayName = Required(accountDisplayName, nameof(accountDisplayName));
        }

        public void ReplaceToken(OneDriveToken token)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }
}
