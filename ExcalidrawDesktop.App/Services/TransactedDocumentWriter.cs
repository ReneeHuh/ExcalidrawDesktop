using ExcalidrawDesktop.Core;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ExcalidrawDesktop.App.Services;

internal static class TransactedDocumentWriter
{
    public static async Task WriteAsync(
        StorageFile file,
        string content,
        CancellationToken cancellationToken,
        string? expectedContentHash = null,
        Action? commitStarted = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var transaction = await file.OpenTransactedWriteAsync().AsTask(cancellationToken);
        if (expectedContentHash is not null)
        {
            if (transaction.Stream.Size > 50L * 1024 * 1024)
                throw new BridgeProtocolException("DocumentTooLarge", "The drawing exceeds the 50 MB desktop document limit.");
            transaction.Stream.Seek(0);
            var hash = await RecoveryFileBaseline.ComputeContentHashAsync(
                transaction.Stream.AsStreamForRead(), cancellationToken);
            if (!string.Equals(hash, expectedContentHash, StringComparison.OrdinalIgnoreCase))
                throw new BridgeProtocolException("DocumentChangedExternally", "The drawing changed outside Excalidraw Desktop. Resolve the conflict before saving.");
            transaction.Stream.Seek(0);
        }
        transaction.Stream.Size = 0;
        transaction.Stream.Seek(0);
        using (var writer = new DataWriter(transaction.Stream)
        { UnicodeEncoding = UnicodeEncoding.Utf8 })
        {
            writer.WriteString(content);
            await writer.StoreAsync().AsTask(cancellationToken);
            await writer.FlushAsync().AsTask(cancellationToken);
            writer.DetachStream();
        }
        cancellationToken.ThrowIfCancellationRequested();
        commitStarted?.Invoke();
        await transaction.CommitAsync();
    }
}
