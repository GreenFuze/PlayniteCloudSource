using System;
using System.IO;
using System.Threading;

namespace CloudSource.Playnite.Installation
{
    internal sealed class ArchiveFileWriter
    {
        public void Write(
            Stream input,
            string destination,
            long expectedSize,
            Action<long> reportProgress,
            CancellationToken cancellationToken)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("A destination is required.", nameof(destination));
            if (expectedSize < 0) throw new ArgumentOutOfRangeException(nameof(expectedSize));

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[128 * 1024];
                long written = 0;
                while (written < expectedSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var requested = (int)Math.Min(buffer.Length, expectedSize - written);
                    var read = input.Read(buffer, 0, requested);
                    if (read == 0)
                    {
                        throw new InvalidDataException(
                            $"Archive entry size mismatch for '{destination}': expected {expectedSize}, received {written}.");
                    }

                    output.Write(buffer, 0, read);
                    written += read;
                    reportProgress?.Invoke(written);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (input.ReadByte() != -1)
                {
                    throw new InvalidDataException(
                        $"Archive entry size mismatch for '{destination}': received more than {expectedSize} bytes.");
                }
            }
        }
    }
}
