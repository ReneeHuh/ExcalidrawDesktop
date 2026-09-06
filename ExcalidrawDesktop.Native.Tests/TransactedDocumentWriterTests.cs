using System.Text;
using ExcalidrawDesktop.App.Services;
using ExcalidrawDesktop.Core;
using Windows.Storage;
using Xunit;

namespace ExcalidrawDesktop.Native.Tests;

public sealed class TransactedDocumentWriterTests
{
    private static async Task<(string Path, StorageFile File)> CreateFileAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"native-write-{Guid.NewGuid():N}.excalidraw");
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
        return (path, await StorageFile.GetFileFromPathAsync(path));
    }

    [Fact]
    public async Task ValidWriteCommitsExactBytes()
    {
        var (path, file) = await CreateFileAsync("before");
        try
        {
            await TransactedDocumentWriter.WriteAsync(file, "after😀", CancellationToken.None);
            Assert.Equal("after😀", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ValidExpectedHashCommitsAndPreservesUtf8Bytes()
    {
        var (path, file) = await CreateFileAsync("before😀");
        try
        {
            var baseline = await ExactFileBaseline.CaptureAsync(path);
            await TransactedDocumentWriter.WriteAsync(
                file, "after背景😀", CancellationToken.None, baseline.ContentHash);
            Assert.Equal(
                Encoding.UTF8.GetBytes("after背景😀"),
                await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExpectedHashMismatchLeavesOriginalIntact()
    {
        var (path, file) = await CreateFileAsync("before");
        try
        {
            var expected = RecoveryFileBaseline.ComputeContentHash("before");
            await File.WriteAllTextAsync(path, "other");
            await Assert.ThrowsAsync<BridgeProtocolException>(() =>
                TransactedDocumentWriter.WriteAsync(file, "replacement", CancellationToken.None, expected));
            Assert.Equal("other", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CancelledBeforeWriteLeavesOriginalIntact()
    {
        var (path, file) = await CreateFileAsync("before");
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                TransactedDocumentWriter.WriteAsync(file, "replacement", cancellation.Token));
            Assert.Equal("before", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CancellationAfterCommitStartsStillFinishesCommit()
    {
        var (path, file) = await CreateFileAsync("before");
        try
        {
            using var cancellation = new CancellationTokenSource();
            await TransactedDocumentWriter.WriteAsync(
                file, "replacement", cancellation.Token,
                null, cancellation.Cancel);
            Assert.Equal("replacement", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ConcurrentWriterIsBlockedWhileTransactionCommits()
    {
        var (path, file) = await CreateFileAsync("before");
        try
        {
            Exception? writerError = null;
            await TransactedDocumentWriter.WriteAsync(
                file,
                "replacement",
                CancellationToken.None,
                null,
                () =>
                {
                    try { File.WriteAllText(path, "other"); }
                    catch (Exception exception) { writerError = exception; }
                });
            Assert.NotNull(writerError);
            Assert.Equal("replacement", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }
}
