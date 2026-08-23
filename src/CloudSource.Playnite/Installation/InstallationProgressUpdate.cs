namespace CloudSource.Playnite.Installation
{
    internal enum InstallationProgressStage
    {
        Downloading,
        Extracting,
        RunningInstaller,
        ValidatingInstallation,
        Finalizing
    }

    internal sealed class InstallationProgressUpdate
    {
        public InstallationProgressStage Stage { get; }
        public long CompletedBytes { get; }
        public long TotalBytes { get; }

        public InstallationProgressUpdate(
            InstallationProgressStage stage,
            long completedBytes = 0,
            long totalBytes = 0)
        {
            Stage = stage;
            CompletedBytes = completedBytes;
            TotalBytes = totalBytes;
        }
    }
}
