using CloudSource.Playnite.Providers;
using CloudSource.Playnite.Installation;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace CloudSource.Playnite.GameImport
{
    internal sealed class CloudGameMetadataFactory
    {
        private readonly GameTitleNormalizer titleNormalizer;
        private readonly CloudArchiveClassifier archiveClassifier;
        private readonly InstallationManifestStore manifestStore;

        public CloudGameMetadataFactory(
            GameTitleNormalizer titleNormalizer,
            CloudArchiveClassifier archiveClassifier,
            InstallationManifestStore manifestStore)
        {
            this.titleNormalizer = titleNormalizer ?? throw new ArgumentNullException(nameof(titleNormalizer));
            this.archiveClassifier = archiveClassifier ?? throw new ArgumentNullException(nameof(archiveClassifier));
            this.manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
        }

        public GameMetadata Create(SourcePackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var rawTitle = Path.GetFileNameWithoutExtension(package.DisplayName);
            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                throw new InvalidDataException($"Cloud package '{package.StableId}' has no usable game name.");
            }

            var installation = manifestStore.Find(package.StableId);
            var metadata = new GameMetadata
            {
                GameId = package.StableId,
                Name = titleNormalizer.CleanDisplayTitle(rawTitle),
                Description = $"Cloud archive: {package.LogicalPath}",
                IsInstalled = installation != null,
                InstallDirectory = installation?.InstallDirectory,
                InstallSize = installation == null ? (ulong?)null : (ulong)installation.Manifest.InstalledSizeBytes,
                Source = new MetadataNameProperty(CloudStorageProduct.DisplayName),
                Version = package.Revision
            };
            var platform = archiveClassifier.ResolvePlatform(package.LogicalPath);
            if (!string.IsNullOrWhiteSpace(platform))
            {
                metadata.Platforms = new HashSet<MetadataProperty>
                {
                    new MetadataNameProperty(platform)
                };
            }

            return metadata;
        }
    }
}
