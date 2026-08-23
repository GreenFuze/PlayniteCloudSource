using System;

namespace CloudSource.Playnite.GameImport
{
    internal sealed class CloudGameIdentity
    {
        public string ProviderId { get; }
        public string AccountId { get; }
        public string ObjectId { get; }
        public string StableId => $"{ProviderId}:{AccountId}:{ObjectId}";

        private CloudGameIdentity(string providerId, string accountId, string objectId)
        {
            ProviderId = providerId;
            AccountId = accountId;
            ObjectId = objectId;
        }

        public static bool TryParse(string stableId, out CloudGameIdentity identity)
        {
            identity = null;
            var parts = (stableId ?? string.Empty).Split(new[] { ':' }, 3);
            if (parts.Length != 3 ||
                string.IsNullOrWhiteSpace(parts[0]) ||
                string.IsNullOrWhiteSpace(parts[1]) ||
                string.IsNullOrWhiteSpace(parts[2]))
            {
                return false;
            }

            identity = new CloudGameIdentity(parts[0], parts[1], parts[2]);
            return true;
        }

        public bool BelongsTo(CloudSourceScope scope)
        {
            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            return string.Equals(ProviderId, scope.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(AccountId, scope.AccountId, StringComparison.Ordinal);
        }
    }

    internal sealed class CloudSourceScope
    {
        public string ProviderId { get; }
        public string AccountId { get; }

        public CloudSourceScope(string providerId, string accountId)
        {
            ProviderId = Required(providerId, nameof(providerId));
            AccountId = Required(accountId, nameof(accountId));
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

    internal sealed class AuthoritativeSourceSnapshot
    {
        public CloudSourceScope Scope { get; }
        public System.Collections.Generic.IReadOnlyCollection<string> GameIds { get; }

        public AuthoritativeSourceSnapshot(
            CloudSourceScope scope,
            System.Collections.Generic.IReadOnlyCollection<string> gameIds)
        {
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            GameIds = gameIds ?? throw new ArgumentNullException(nameof(gameIds));
        }
    }
}
