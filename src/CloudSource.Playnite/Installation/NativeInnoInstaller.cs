using CloudSource.Playnite.Providers;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace CloudSource.Playnite.Installation
{
    internal interface INativeInstallerProcessRunner
    {
        int Run(string path, string arguments, string workingDirectory);
    }

    internal sealed class WindowsNativeInstallerProcessRunner : INativeInstallerProcessRunner
    {
        public int Run(string path, string arguments, string workingDirectory)
        {
            var start = new ProcessStartInfo
            {
                FileName = path,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process process;
            try
            {
                process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start the installer process.");
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("Windows permission request was declined.", exception);
            }

            using (process)
            {
                while (!process.WaitForExit(500))
                {
                    // Once a native installer starts, do not terminate it from the
                    // Playnite progress dialog; interruption can corrupt its install.
                }

                return process.ExitCode;
            }
        }
    }

    internal sealed class NativeInnoInstallResult
    {
        public string LaunchTarget { get; }
        public string UninstallTarget { get; }
        public int ExitCode { get; }

        public NativeInnoInstallResult(string launchTarget, string uninstallTarget, int exitCode)
        {
            LaunchTarget = launchTarget;
            UninstallTarget = uninstallTarget;
            ExitCode = exitCode;
        }
    }

    internal sealed class NativeInnoInstaller
    {
        private readonly LaunchTargetResolver launchTargetResolver;
        private readonly INativeInstallerProcessRunner processRunner;

        public NativeInnoInstaller(LaunchTargetResolver launchTargetResolver)
            : this(launchTargetResolver, new WindowsNativeInstallerProcessRunner())
        {
        }

        internal NativeInnoInstaller(
            LaunchTargetResolver launchTargetResolver,
            INativeInstallerProcessRunner processRunner)
        {
            this.launchTargetResolver = launchTargetResolver ?? throw new ArgumentNullException(nameof(launchTargetResolver));
            this.processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        }

        public NativeInnoInstallResult Install(
            PreparedPayloadClassification classification,
            string gameName,
            string destination,
            Func<InstallerConfirmationRequest, bool> confirm,
            Func<LaunchTargetSelectionRequest, string> selectLaunchTarget,
            Action<InstallationProgressUpdate> reportProgress,
            CancellationToken cancellationToken)
        {
            if (classification == null || classification.Kind != PreparedPayloadKind.InnoInstaller)
                throw new ArgumentException("An Inno installer classification is required.", nameof(classification));
            if (confirm == null) throw new InvalidOperationException("Native installer confirmation is unavailable.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!confirm(new InstallerConfirmationRequest(
                gameName,
                Path.GetFileName(classification.InstallerPath),
                destination,
                classification.SignerSubject)))
            {
                throw new OperationCanceledException("Native installer execution was declined.");
            }

            reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.RunningInstaller, 0, 1));
            var exitCode = processRunner.Run(
                classification.InstallerPath,
                "/DIR=" + Quote(destination) + " /NORESTART",
                Path.GetDirectoryName(classification.InstallerPath));
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Installer exited with code {exitCode}.");
            }

            return FinalizeExistingInstallation(
                gameName,
                destination,
                selectLaunchTarget,
                reportProgress,
                exitCode);
        }

        public bool CanFinalizeExistingInstallation(string destination)
        {
            try
            {
                return Directory.Exists(destination) &&
                    !string.IsNullOrWhiteSpace(ResolveUninstaller(destination)) &&
                    launchTargetResolver.Discover(destination, Path.GetFileName(destination)).Count > 0;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        public NativeInnoInstallResult FinalizeExistingInstallation(
            string gameName,
            string destination,
            Func<LaunchTargetSelectionRequest, string> selectLaunchTarget,
            Action<InstallationProgressUpdate> reportProgress,
            int exitCode = 0)
        {
            reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.ValidatingInstallation, 0, 1));
            if (!Directory.Exists(destination))
            {
                throw new InvalidDataException("The installer completed, but the managed destination was not created.");
            }

            var uninstallTarget = ResolveUninstaller(destination);
            var launchTarget = launchTargetResolver.Resolve(destination, gameName, selectLaunchTarget);
            reportProgress?.Invoke(new InstallationProgressUpdate(InstallationProgressStage.ValidatingInstallation, 1, 1));
            return new NativeInnoInstallResult(launchTarget, uninstallTarget, exitCode);
        }

        public void Uninstall(string installDirectory, string uninstallTarget)
        {
            var target = ResolveMember(installDirectory, uninstallTarget);
            if (!File.Exists(target)) throw new FileNotFoundException("The recorded Inno uninstaller is missing.", target);
            // Keep the vendor uninstaller visible. Some games ask whether saves or
            // configuration should be retained, and unattended defaults must not
            // make that decision for the user.
            var exitCode = processRunner.Run(target, "/NORESTART", installDirectory);
            if (exitCode != 0) throw new InvalidOperationException($"Uninstaller exited with code {exitCode}.");
        }

        private static string ResolveUninstaller(string destination)
        {
            var candidates = Directory.EnumerateFiles(destination, "unins*.exe", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count != 1)
            {
                throw new InvalidDataException(candidates.Count == 0
                    ? "The installed game has no Inno uninstaller."
                    : "The installed game has multiple Inno uninstallers; ownership is ambiguous.");
            }

            return MakeRelative(destination, candidates[0]);
        }

        private static string ResolveMember(string root, string relative)
        {
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, relative ?? string.Empty));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Recorded installer path escapes the managed game directory.");
            return path;
        }

        private static string MakeRelative(string root, string path)
        {
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installer output escapes the managed game directory.");
            return full.Substring(prefix.Length);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }
}
