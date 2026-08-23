using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CloudSource.Playnite.Installation
{
    internal enum PreparedPayloadKind
    {
        ReadyToRun,
        InnoInstaller
    }

    internal sealed class PreparedPayloadClassification
    {
        public PreparedPayloadKind Kind { get; }
        public string InstallerPath { get; }
        public string SignerSubject { get; }

        public PreparedPayloadClassification(
            PreparedPayloadKind kind,
            string installerPath = null,
            string signerSubject = null)
        {
            Kind = kind;
            InstallerPath = installerPath;
            SignerSubject = signerSubject;
        }
    }

    internal sealed class InstallerPackageClassifier
    {
        private const int DetectionWindowBytes = 16 * 1024 * 1024;

        public PreparedPayloadClassification Classify(string payloadRoot)
        {
            if (string.IsNullOrWhiteSpace(payloadRoot) || !Directory.Exists(payloadRoot))
            {
                throw new DirectoryNotFoundException("Prepared package payload does not exist.");
            }

            var namedSetupCandidates = Directory
                .EnumerateFiles(payloadRoot, "*.exe", SearchOption.AllDirectories)
                .Where(path => IsSetupName(Path.GetFileName(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (namedSetupCandidates.Count == 0)
            {
                return new PreparedPayloadClassification(PreparedPayloadKind.ReadyToRun);
            }

            var innoCandidates = namedSetupCandidates.Where(IsInnoSetup).ToList();
            if (innoCandidates.Count != 1)
            {
                throw new InvalidDataException(
                    innoCandidates.Count == 0
                        ? "The package contains a setup executable, but it is not a supported Inno Setup installer."
                        : $"The package contains {innoCandidates.Count} Inno Setup installers; automatic selection was stopped.");
            }

            return new PreparedPayloadClassification(
                PreparedPayloadKind.InnoInstaller,
                innoCandidates[0],
                TryGetSignerSubject(innoCandidates[0]));
        }

        private static bool IsSetupName(string name)
        {
            var stem = Path.GetFileNameWithoutExtension(name ?? string.Empty);
            return string.Equals(stem, "setup", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("setup_", StringComparison.OrdinalIgnoreCase) ||
                stem.EndsWith("setup", StringComparison.OrdinalIgnoreCase) ||
                stem.EndsWith("installer", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInnoSetup(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 2 || stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') return false;
                return ContainsMarker(stream, 0, Math.Min(stream.Length, DetectionWindowBytes)) ||
                    (stream.Length > DetectionWindowBytes &&
                     ContainsMarker(stream, Math.Max(0, stream.Length - DetectionWindowBytes), Math.Min(stream.Length, DetectionWindowBytes)));
            }
        }

        private static bool ContainsMarker(Stream stream, long offset, long count)
        {
            stream.Position = offset;
            var marker = Encoding.ASCII.GetBytes("Inno Setup");
            var buffer = new byte[128 * 1024];
            var matched = 0;
            long remaining = count;
            while (remaining > 0)
            {
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) break;
                remaining -= read;
                for (var index = 0; index < read; index++)
                {
                    matched = buffer[index] == marker[matched] ? matched + 1 : (buffer[index] == marker[0] ? 1 : 0);
                    if (matched == marker.Length) return true;
                }
            }

            return false;
        }

        private static string TryGetSignerSubject(string path)
        {
            try
            {
                using (var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                {
                    return certificate.Subject;
                }
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
    }

    internal sealed class InstallerConfirmationRequest
    {
        public string GameName { get; }
        public string InstallerName { get; }
        public string Destination { get; }
        public string SignerSubject { get; }

        public InstallerConfirmationRequest(string gameName, string installerName, string destination, string signerSubject)
        {
            GameName = gameName;
            InstallerName = installerName;
            Destination = destination;
            SignerSubject = signerSubject;
        }
    }
}
