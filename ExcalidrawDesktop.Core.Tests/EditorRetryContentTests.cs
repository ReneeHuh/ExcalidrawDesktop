using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class EditorRetryContentTests
{
    private const string Saved = """{"type":"excalidraw","elements":[{"id":"saved"}]}""";
    private const string Unsaved = """{"type":"excalidraw","elements":[{"id":"unsaved"}],"files":{"image":{"dataURL":"data:image/png;base64,example"}}}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreservesPendingLoadWhenAnEarlierInitializationFailed(bool isRecovery)
    {
        var pending = new PendingEditorLoad("pending.excalidraw", Unsaved, isRecovery);
        var result = await EditorRetryContent.LoadAsync(
            new EditorRetryState("drawing", true, true, pending),
            UnexpectedRecoveryRead,
            UnexpectedFileRead);

        Assert.Same(pending, result);
    }

    [Fact]
    public async Task RestoresDirtyContentAndEmbeddedFilesInsteadOfReadingTheSavedFile()
    {
        var result = await EditorRetryContent.LoadAsync(
            new EditorRetryState("drawing.excalidraw", true, true, HibernatedContent: Saved),
            () => Task.FromResult<string?>(Unsaved),
            UnexpectedFileRead);

        Assert.Equal(new PendingEditorLoad("drawing.excalidraw", Unsaved, true), result);
    }

    [Fact]
    public async Task RestoresAnUnsavedUntitledDrawing()
    {
        var result = await EditorRetryContent.LoadAsync(
            new EditorRetryState("Untitled", true, false),
            () => Task.FromResult<string?>(Unsaved),
            UnexpectedFileRead);

        Assert.Equal(new PendingEditorLoad("Untitled", Unsaved, true), result);
    }

    [Fact]
    public async Task ReloadsCleanDrawingFromItsFile()
    {
        var saved = new PendingEditorLoad("drawing.excalidraw", Saved, false);
        var result = await EditorRetryContent.LoadAsync(
            new EditorRetryState("drawing.excalidraw", false, true),
            UnexpectedRecoveryRead,
            () => Task.FromResult(saved));

        Assert.Same(saved, result);
    }

    [Fact]
    public async Task PreservesHibernatedContentWhileRecreatingAnUnloadedEditor()
    {
        var result = await EditorRetryContent.LoadAsync(
            new EditorRetryState("drawing.excalidraw", false, true, HibernatedContent: Saved),
            UnexpectedRecoveryRead,
            UnexpectedFileRead);

        Assert.Equal(new PendingEditorLoad("drawing.excalidraw", Saved, false), result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingSnapshotDoesNotFallBackToAnEmptyOrSavedDrawing(bool hasActiveFile)
    {
        var error = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
            EditorRetryContent.LoadAsync(
                new EditorRetryState("drawing", true, hasActiveFile),
                () => Task.FromResult<string?>(null),
                UnexpectedFileRead));

        Assert.Equal("EditorRecoveryUnavailable", error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid json")]
    [InlineData("{\"type\":\"excalidraw\"}")]
    public async Task InvalidRecoveryContentDoesNotFallBackToTheSavedDrawing(string content)
    {
        await Assert.ThrowsAsync<BridgeProtocolException>(() => EditorRetryContent.LoadAsync(
            new EditorRetryState("drawing", true, true),
            () => Task.FromResult<string?>(content),
            UnexpectedFileRead));
    }

    [Fact]
    public async Task FailedFileReadDoesNotProduceAnEmptyEditor()
    {
        await Assert.ThrowsAsync<IOException>(() => EditorRetryContent.LoadAsync(
            new EditorRetryState("drawing", false, true),
            UnexpectedRecoveryRead,
            () => throw new IOException("The file cannot be read.")));
    }

    [Fact]
    public async Task InvalidPendingContentIsRejectedBeforeReplacingTheEditor()
    {
        await Assert.ThrowsAsync<BridgeProtocolException>(() => EditorRetryContent.LoadAsync(
            new EditorRetryState("drawing", false, true,
                new PendingEditorLoad("drawing", "invalid json", false)),
            UnexpectedRecoveryRead,
            UnexpectedFileRead));
    }

    [Fact]
    public async Task AnUntouchedUntitledTabCanStartEmpty()
    {
        var result = await EditorRetryContent.LoadAsync(
            new EditorRetryState("Untitled", false, false),
            UnexpectedRecoveryRead,
            UnexpectedFileRead);

        Assert.Null(result);
    }

    private static Task<string?> UnexpectedRecoveryRead() =>
        throw new InvalidOperationException("Recovery content should not be read.");

    private static Task<PendingEditorLoad> UnexpectedFileRead() =>
        throw new InvalidOperationException("The saved file should not be read.");
}
