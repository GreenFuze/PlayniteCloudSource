using System;
using System.Text.RegularExpressions;

namespace CloudSource.Playnite.GameImport
{
    internal sealed class GameTitleNormalizer
    {
        private static readonly Regex TrailingBracketNoise = new Regex(
            @"[\s._-]*[\(\[][^\)\]]*[\)\]]\s*$",
            RegexOptions.Compiled);
        private static readonly Regex SetupPrefix = new Regex(
            @"^setup[\s._-]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SetupInstallerVersionSuffix = new Regex(
            @"[\s._-]+\d+(?:\.\d+)+(?:[\s._-]+[a-z]{2,8}\d*)*\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VersionSuffix = new Regex(
            @"[\s._-]+(?:(?:v|version[\s._-]*)\d+(?:\.\d+)+(?:[\s._-]+[a-z]{2,8}\d*)*|\d+(?:\.\d+)+[\s._-]+[a-z]{2,8}\d*(?:[\s._-]+[a-z]{2,8}\d*)*)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WordSeparatorDots = new Regex(
            @"(?<=[\p{L}])\.|\.(?=[\p{L}])",
            RegexOptions.Compiled);
        private static readonly Regex MultiSpace = new Regex(@"\s+", RegexOptions.Compiled);

        public string CleanDisplayTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A game title is required.", nameof(value));
            }

            value = value.Trim();
            var setupInstaller = SetupPrefix.IsMatch(value);
            value = StripTrailingLookupNoise(value);
            if (setupInstaller)
            {
                value = SetupInstallerVersionSuffix.Replace(value, string.Empty);
            }

            value = SetupPrefix.Replace(value, string.Empty);
            value = value.Replace('_', ' ');
            value = WordSeparatorDots.Replace(value, " ");
            value = MultiSpace.Replace(value, " ").Trim(' ', '.', '_', '-');
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("The archive filename contains no usable game title.");
            }

            return value;
        }

        private static string StripTrailingLookupNoise(string value)
        {
            while (true)
            {
                var changed = false;
                if (TrailingBracketNoise.IsMatch(value))
                {
                    value = TrailingBracketNoise.Replace(value, string.Empty);
                    changed = true;
                }

                var withoutVersion = VersionSuffix.Replace(value, string.Empty);
                if (!string.Equals(withoutVersion, value, StringComparison.Ordinal))
                {
                    value = withoutVersion;
                    changed = true;
                }

                if (!changed)
                {
                    return value.Trim();
                }
            }
        }
    }
}
