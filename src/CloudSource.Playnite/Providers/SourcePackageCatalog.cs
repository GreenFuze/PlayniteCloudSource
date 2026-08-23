using System;
using System.Collections.Generic;

namespace CloudSource.Playnite.Providers
{
    internal sealed class SourcePackageCatalog
    {
        private readonly object sync = new object();
        private readonly Dictionary<string, SourcePackage> packages =
            new Dictionary<string, SourcePackage>(StringComparer.Ordinal);

        public void ReplaceScope(string providerId, string accountId, IEnumerable<SourcePackage> replacements)
        {
            if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Provider ID is required.", nameof(providerId));
            if (string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("Account ID is required.", nameof(accountId));
            if (replacements == null) throw new ArgumentNullException(nameof(replacements));

            lock (sync)
            {
                var prefix = providerId + ":" + accountId + ":";
                var stale = new List<string>();
                foreach (var key in packages.Keys)
                {
                    if (key.StartsWith(prefix, StringComparison.Ordinal)) stale.Add(key);
                }

                foreach (var key in stale) packages.Remove(key);
                foreach (var package in replacements)
                {
                    if (package == null) throw new ArgumentException("Package catalog cannot contain null entries.", nameof(replacements));
                    packages[package.StableId] = package;
                }
            }
        }

        public bool TryGet(string stableId, out SourcePackage package)
        {
            lock (sync)
            {
                return packages.TryGetValue(stableId ?? string.Empty, out package);
            }
        }
    }
}
