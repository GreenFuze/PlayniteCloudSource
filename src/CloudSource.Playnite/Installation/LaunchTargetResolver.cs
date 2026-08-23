using CloudSource.Playnite.GameImport;
using System;
using System.IO;
using System.Linq;

namespace CloudSource.Playnite.Installation
{
    internal sealed class LaunchTargetResolver
    {
        private static readonly string[] ExcludedFragments =
        {
            "unins", "uninstall", "setup", "installer", "update", "updater",
            "vcredist", "dxsetup", "crashhandler", "crashreport", "helper"
        };
        private readonly GameTitleNormalizer titleNormalizer;

        public LaunchTargetResolver(GameTitleNormalizer titleNormalizer)
        {
            this.titleNormalizer = titleNormalizer ?? throw new ArgumentNullException(nameof(titleNormalizer));
        }

        public string Resolve(string payloadRoot, string gameName)
        {
            var normalizedTitle = Normalize(gameName);
            var candidates = Directory.EnumerateFiles(payloadRoot, "*.exe", SearchOption.AllDirectories)
                .Where(path => !IsExcluded(path))
                .Select(path => new LaunchCandidate(path, Score(payloadRoot, path, normalizedTitle)))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0)
            {
                throw new InvalidDataException("The extracted ZIP contains no launchable executable.");
            }

            if (candidates.Count > 1 && candidates[0].Score - candidates[1].Score < 50)
            {
                var names = string.Join(", ", candidates.Take(5).Select(candidate => Path.GetFileName(candidate.Path)));
                throw new InvalidDataException($"The game has multiple plausible executables ({names}). Automatic selection was intentionally stopped.");
            }

            return MakeRelativePath(payloadRoot, candidates[0].Path);
        }

        private int Score(string root, string path, string normalizedTitle)
        {
            var executable = Normalize(Path.GetFileNameWithoutExtension(path));
            var relative = MakeRelativePath(root, path);
            var depth = relative.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar);
            var score = -25 * depth;
            if (string.Equals(executable, normalizedTitle, StringComparison.Ordinal))
            {
                score += 1000;
            }
            else if (executable.Contains(normalizedTitle) || normalizedTitle.Contains(executable))
            {
                score += 300;
            }

            return score;
        }

        private string Normalize(string value)
        {
            var cleaned = titleNormalizer.CleanDisplayTitle(value ?? string.Empty);
            return new string(cleaned.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private static bool IsExcluded(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path)?.ToLowerInvariant() ?? string.Empty;
            return ExcludedFragments.Any(fragment => fileName.Contains(fragment));
        }

        private static string MakeRelativePath(string root, string path)
        {
            var prefix = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Launch target is outside the extracted game directory.");
            }

            return fullPath.Substring(prefix.Length);
        }

        private sealed class LaunchCandidate
        {
            public string Path { get; }
            public int Score { get; }

            public LaunchCandidate(string path, int score)
            {
                Path = path;
                Score = score;
            }
        }
    }
}
