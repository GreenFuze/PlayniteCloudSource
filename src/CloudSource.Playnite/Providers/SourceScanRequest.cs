using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudSource.Playnite.Providers
{
    public sealed class SourceScanRequest
    {
        public string AccountId { get; }
        public IReadOnlyList<SourceLocation> Locations { get; }

        public SourceScanRequest(string accountId, IEnumerable<SourceLocation> locations)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                throw new ArgumentException("Account ID is required.", nameof(accountId));
            }

            if (locations == null)
            {
                throw new ArgumentNullException(nameof(locations));
            }

            var locationList = locations.ToList();
            if (locationList.Count == 0 || locationList.Any(location => location == null))
            {
                throw new ArgumentException("At least one valid source location is required.", nameof(locations));
            }

            AccountId = accountId.Trim();
            Locations = locationList;
        }
    }

    public sealed class SourceLocation
    {
        public string ObjectId { get; }
        public string DisplayPath { get; }
        public bool Recursive { get; }

        public SourceLocation(string objectId, string displayPath, bool recursive)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                throw new ArgumentException("Provider object ID is required.", nameof(objectId));
            }

            if (string.IsNullOrWhiteSpace(displayPath))
            {
                throw new ArgumentException("Display path is required.", nameof(displayPath));
            }

            ObjectId = objectId.Trim();
            DisplayPath = displayPath.Trim();
            Recursive = recursive;
        }
    }
}
