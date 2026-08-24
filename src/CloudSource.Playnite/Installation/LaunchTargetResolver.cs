using CloudSource.Playnite.GameImport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CloudSource.Playnite.Installation
{
    internal sealed class LaunchTargetCandidate
    {
        public string RelativePath { get; }
        public string FileName => Path.GetFileName(RelativePath);
        internal int Score { get; }

        public LaunchTargetCandidate(string relativePath, int score)
        {
            RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
            Score = score;
        }
    }

    internal sealed class LaunchTargetSelectionRequest
    {
        public string GameName { get; }
        public string InstallDirectory { get; }
        public IReadOnlyList<LaunchTargetCandidate> Candidates { get; }

        public LaunchTargetSelectionRequest(
            string gameName,
            string installDirectory,
            IReadOnlyList<LaunchTargetCandidate> candidates)
        {
            GameName = gameName ?? throw new ArgumentNullException(nameof(gameName));
            InstallDirectory = installDirectory ?? throw new ArgumentNullException(nameof(installDirectory));
            Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        }
    }

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

        public string Resolve(
            string payloadRoot,
            string gameName,
            Func<LaunchTargetSelectionRequest, string> selectCandidate = null)
        {
            var candidates = Discover(payloadRoot, gameName);
            if (candidates.Count == 0)
            {
                throw new InvalidDataException("The installed game contains no launchable executable.");
            }

            if (candidates.Count == 1)
            {
                return candidates[0].RelativePath;
            }

            if (selectCandidate == null)
            {
                if (candidates[0].Score - candidates[1].Score >= 50)
                {
                    return candidates[0].RelativePath;
                }

                var names = string.Join(", ", candidates.Take(5).Select(candidate => candidate.FileName));
                throw new InvalidDataException($"The game has multiple plausible launchers ({names}). Automatic selection was intentionally stopped.");
            }

            var selected = selectCandidate(new LaunchTargetSelectionRequest(gameName, payloadRoot, candidates));
            if (string.IsNullOrWhiteSpace(selected))
            {
                throw new OperationCanceledException("Game launcher selection was canceled.");
            }

            var match = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.RelativePath, selected, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                throw new InvalidDataException("The selected launcher is not one of the validated launch candidates.");
            }

            return match.RelativePath;
        }

        public IReadOnlyList<LaunchTargetCandidate> Discover(string payloadRoot, string gameName)
        {
            if (string.IsNullOrWhiteSpace(payloadRoot) || !Directory.Exists(payloadRoot))
            {
                throw new DirectoryNotFoundException("Installed game directory does not exist.");
            }

            var normalizedTitle = Normalize(gameName);
            return Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories)
                .Where(IsSupportedLaunchFile)
                .Where(path => !IsExcluded(path))
                .Select(path => new LaunchTargetCandidate(
                    MakeRelativePath(payloadRoot, path),
                    Score(payloadRoot, path, normalizedTitle)))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
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

        private static bool IsSupportedLaunchFile(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase);
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
    }
}
