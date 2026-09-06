using System.Text;

namespace ExcalidrawDesktop.Core;

/// <summary>A file version tied to the exact bytes captured from disk.</summary>
public sealed record ExactFileBaseline(DesktopFileStamp Stamp, string ContentHash, byte[] Bytes)
{
    public const long DefaultMaxBytes = 50L * 1024 * 1024;
    public string Utf8Content
    {
        get
        {
            var content = new UTF8Encoding(false, true).GetString(Bytes);
            return content.Length > 0 && content[0] == '\uFEFF' ? content[1..] : content;
        }
    }

    public bool Matches(DesktopFileStamp stamp, ReadOnlySpan<byte> bytes) =>
        Stamp == stamp &&
        string.Equals(ContentHash, RecoveryFileBaseline.ComputeContentHash(bytes), StringComparison.OrdinalIgnoreCase);

    public static async Task<ExactFileBaseline> CaptureAsync(
        string path,
        int maxAttempts = 3,
        long maxBytes = DefaultMaxBytes,
        CancellationToken cancellationToken = default)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Keep a shared read handle while capturing bytes. Writers that
            // honor Windows sharing semantics cannot replace the file between
            // the byte read and its stamp.
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var before = ReadStamp(path);
            if (before.Size > (ulong)maxBytes)
                throw new IOException("The file exceeds the baseline capture limit.");
            if (stream.Length > maxBytes || stream.Length > int.MaxValue)
                throw new IOException("The file exceeds the baseline capture limit.");
            var bytes = new byte[checked((int)stream.Length)];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            var after = ReadStamp(path);
            if (before == after)
            {
                return new ExactFileBaseline(
                    after,
                    RecoveryFileBaseline.ComputeContentHash(bytes),
                    bytes);
            }
        }
        throw new IOException("The file changed while it was being read.");
    }

    public static DesktopFileStamp ReadStamp(string path)
    {
        var info = new FileInfo(path);
        return new DesktopFileStamp(info.LastWriteTimeUtc, (ulong)info.Length);
    }
}
