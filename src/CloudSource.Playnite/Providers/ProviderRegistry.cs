using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudSource.Playnite.Providers
{
    public sealed class ProviderRegistry
    {
        private readonly IReadOnlyDictionary<string, ICloudSourceProvider> providers;

        public int Count => providers.Count;

        public ProviderRegistry(IEnumerable<ICloudSourceProvider> providers)
        {
            if (providers == null)
            {
                throw new ArgumentNullException(nameof(providers));
            }

            var providerList = providers.ToList();
            if (providerList.Any(provider => provider == null))
            {
                throw new ArgumentException("Provider registry cannot contain null providers.", nameof(providers));
            }

            var invalid = providerList.FirstOrDefault(provider => string.IsNullOrWhiteSpace(provider.Id));
            if (invalid != null)
            {
                throw new ArgumentException("Every provider must have a non-empty ID.", nameof(providers));
            }

            var duplicate = providerList
                .GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                throw new ArgumentException($"Provider ID '{duplicate.Key}' is registered more than once.", nameof(providers));
            }

            this.providers = providerList.ToDictionary(
                provider => provider.Id,
                provider => provider,
                StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<ICloudSourceProvider> GetConfiguredProviders()
        {
            return providers.Values.Where(provider => provider.IsConfigured).ToList();
        }

        public ICloudSourceProvider GetRequired(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("Provider ID is required.", nameof(providerId));
            }

            if (!providers.TryGetValue(providerId, out var provider))
            {
                throw new KeyNotFoundException($"Provider '{providerId}' is not registered.");
            }

            return provider;
        }
    }
}
