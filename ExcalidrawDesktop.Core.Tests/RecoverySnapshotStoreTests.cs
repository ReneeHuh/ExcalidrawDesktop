using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class RecoverySnapshotStoreTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ExcalidrawRecoveryTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SavesUtf8RecoveryWithValidatedMetadataAndRawSceneValues()
    {
        var store = new RecoverySnapshotStore(testDirectory);
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(testDirectory, "drawing.excalidraw");
        var baseline = new RecoveryFileBaseline(path, new DesktopFileStamp(DateTimeOffset.UtcNow, 123), "hash");
        const string content = """{"text":"背景色-😀","number":1.2300e+02,"desktopRecovery":{"path":"wrong"}}""";
        await store.SaveAsync(id, content, baseline);
        var stored = Assert.IsType<string>(await store.LoadAsync(id));
        Assert.Equal(baseline, RecoveryFileBaseline.ReadEmbedded(stored));
        Assert.Contains("背景色-😀", stored);
        Assert.Contains("1.2300e+02", stored);
        Assert.DoesNotContain("wrong", stored);
    }

    [Fact]
    public async Task SavesLoadsDeletesAndPrunesSnapshots()
    {
        var store = new RecoverySnapshotStore(testDirectory);
        var retained = Guid.NewGuid().ToString("N");
        var removed = Guid.NewGuid().ToString("N");

        await store.SaveAsync(retained, "retained");
        await store.SaveAsync(removed, "removed");
        Assert.Equal("retained", await store.LoadAsync(retained));

        await store.PruneExceptAsync(new[] { retained });
        Assert.Equal("retained", await store.LoadAsync(retained));
        Assert.Null(await store.LoadAsync(removed));

        await store.DeleteAsync(retained);
        Assert.Null(await store.LoadAsync(retained));
    }

    [Fact]
    public async Task PruneRechecksRetentionAfterAnAlreadyQueuedSnapshotWrite()
    {
        var store = new RecoverySnapshotStore(testDirectory);
        var id = Guid.NewGuid().ToString("N");
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        // Hold a first prune in its callback, then queue a write and a second
        // prune in that order. No filesystem sleeps or timing assumptions.
        var firstPrune = Task.Run(() => store.PruneExceptAsync(() =>
        {
            entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return Array.Empty<string>();
        }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var retained = new List<string>();
        var save = store.SaveAsync(id, "new drawing");
        var checkedRetention = false;
        var prune = store.PruneExceptAsync(() =>
        {
            checkedRetention = true;
            Assert.True(File.Exists(Path.Combine(testDirectory, $"{id}.excalidraw")));
            return retained;
        });
        Assert.False(checkedRetention);
        retained.Add(id);
        release.Set();
        await Task.WhenAll(firstPrune, save, prune);
        Assert.Equal("new drawing", await store.LoadAsync(id));
    }

    [Fact]
    public async Task PersistsTheOriginalBaselineAcrossStoreInstances()
    {
        var id = Guid.NewGuid().ToString("N");
        var baseline = new RecoveryFileBaseline(@"C:\Drawings\one.excalidraw",
            new DesktopFileStamp(DateTimeOffset.UtcNow, 321));
        await new RecoverySnapshotStore(testDirectory).SaveAsync(id,
            """{"type":"excalidraw","elements":[]}""", baseline);
        var restored = await new RecoverySnapshotStore(testDirectory).LoadAsync(id);
        Assert.NotNull(restored);
        Assert.Equal(baseline.Stamp, RecoveryFileBaseline.ReadStamp(restored, baseline.Path));
    }

    [Fact]
    public async Task RejectsIdentifiersThatCouldEscapeTheRecoveryDirectory()
    {
        var store = new RecoverySnapshotStore(testDirectory);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync("../snapshot", "content"));
    }

    [Fact]
    public async Task DiscoversOrphansAndRepeatedPruneKeepsExplicitlyRetainedSnapshot()
    {
        var retained = Guid.NewGuid().ToString("N");
        var orphan = Guid.NewGuid().ToString("N");
        var store = new RecoverySnapshotStore(testDirectory);
        await store.SaveAsync(retained, "keep");
        await store.SaveAsync(orphan, "orphan");
        Assert.Contains(orphan, store.DiscoverSnapshotIds());
        await store.PruneExceptAsync(new[] { retained });
        await store.PruneExceptAsync(new[] { retained });
        Assert.Equal("keep", await store.LoadAsync(retained));
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}
