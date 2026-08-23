using CloudSource.Playnite.Providers;
using System;
using System.Collections.Generic;

namespace CloudSource.Playnite.Installation
{
    internal sealed class ArchiveExtractorRegistry
    {
        private readonly IReadOnlyDictionary<SourcePackageKind, IArchiveExtractor> extractors;

        public ArchiveExtractorRegistry(IEnumerable<IArchiveExtractor> extractors)
        {
            if (extractors == null)
            {
                throw new ArgumentNullException(nameof(extractors));
            }

            var byKind = new Dictionary<SourcePackageKind, IArchiveExtractor>();
            foreach (var extractor in extractors)
            {
                if (extractor == null)
                {
                    throw new ArgumentException("Archive extractors cannot contain null entries.", nameof(extractors));
                }

                if (byKind.ContainsKey(extractor.Kind))
                {
                    throw new ArgumentException($"More than one extractor is registered for {extractor.Kind}.", nameof(extractors));
                }

                byKind.Add(extractor.Kind, extractor);
            }

            this.extractors = byKind;
        }

        public IArchiveExtractor GetRequired(SourcePackageKind kind)
        {
            if (!extractors.TryGetValue(kind, out var extractor))
            {
                throw new NotSupportedException($"Cloud Storage cannot install archive kind '{kind}'.");
            }

            return extractor;
        }
    }
}
