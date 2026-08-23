using System;
using System.Globalization;
using System.Runtime.Serialization;

namespace CloudSource.Playnite.Installation
{
    [DataContract]
    internal sealed class InstallManifest
    {
        [DataMember(Order = 1)]
        public int SchemaVersion { get; set; } = 1;
        [DataMember(Order = 2)]
        public string GameId { get; set; }
        [DataMember(Order = 3)]
        public string GameName { get; set; }
        [DataMember(Order = 4)]
        public string ProviderId { get; set; }
        [DataMember(Order = 5)]
        public string AccountId { get; set; }
        [DataMember(Order = 6)]
        public string ObjectId { get; set; }
        [DataMember(Order = 7)]
        public string Revision { get; set; }
        [DataMember(Order = 8)]
        public string LogicalPath { get; set; }
        [DataMember(Order = 9)]
        public string ArchiveSha256 { get; set; }
        [DataMember(Order = 10)]
        public long ArchiveSizeBytes { get; set; }
        [DataMember(Order = 11)]
        public long InstalledSizeBytes { get; set; }
        [DataMember(Order = 12)]
        public string LaunchTarget { get; set; }
        [DataMember(Order = 13)]
        public string InstalledAtUtc { get; set; } = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
    }

    internal sealed class InstallationRecord
    {
        public string InstallDirectory { get; }
        public InstallManifest Manifest { get; }

        public InstallationRecord(string installDirectory, InstallManifest manifest)
        {
            InstallDirectory = installDirectory ?? throw new ArgumentNullException(nameof(installDirectory));
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        }

        public string LaunchPath => System.IO.Path.Combine(InstallDirectory, Manifest.LaunchTarget);
    }
}
