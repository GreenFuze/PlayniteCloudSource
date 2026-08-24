using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudSource.Playnite.Providers
{
    public sealed class CloudProviderAccount
    {
        public string Id { get; }
        public string DisplayName { get; }

        public CloudProviderAccount(string id, string displayName)
        {
            Id = Required(id, nameof(id));
            DisplayName = Required(displayName, nameof(displayName));
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }

    public sealed class CloudProviderFolder
    {
        public string ObjectId { get; }
        public string DisplayPath { get; }

        public CloudProviderFolder(string objectId, string displayPath)
        {
            ObjectId = Required(objectId, nameof(objectId));
            DisplayPath = Required(displayPath, nameof(displayPath));
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }

    public sealed class CloudProviderScanResult
    {
        public string ProviderId { get; }
        public string AccountId { get; }
        public IReadOnlyList<SourcePackage> Packages { get; }

        public CloudProviderScanResult(
            string providerId,
            string accountId,
            IEnumerable<SourcePackage> packages)
        {
            ProviderId = Required(providerId, nameof(providerId));
            AccountId = Required(accountId, nameof(accountId));
            if (packages == null) throw new ArgumentNullException(nameof(packages));
            var packageList = packages.ToList();
            if (packageList.Any(package => package == null))
                throw new ArgumentException("Scan result cannot contain null packages.", nameof(packages));
            if (packageList.Any(package =>
                !string.Equals(package.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(package.AccountId, AccountId, StringComparison.Ordinal)))
            {
                throw new ArgumentException("Every package must belong to the scan result provider and account.", nameof(packages));
            }

            Packages = packageList.AsReadOnly();
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }
}
