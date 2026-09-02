using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class RecoverySnapshotStoreTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ExcalidrawRecoveryTests-{Guid.NewGuid():N}");

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
    public async Task RejectsIdentifiersThatCouldEscapeTheRecoveryDirectory()
    {
        var store = new RecoverySnapshotStore(testDirectory);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync("../snapshot", "content"));
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}
