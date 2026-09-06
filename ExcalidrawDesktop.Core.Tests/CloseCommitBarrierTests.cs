using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Core.Tests;

public sealed class CloseCommitBarrierTests
{
    [Fact]
    public async Task EditWhileWorkspaceIsPersistingPreventsPruningAndClose()
    {
        var dirty = false;
        var pruned = false;
        var closed = await CloseCommitBarrier.CompleteAsync(
            () => !dirty,
            () => { dirty = true; return Task.CompletedTask; },
            () => { pruned = true; return Task.CompletedTask; });
        Assert.False(closed);
        Assert.False(pruned);
    }

    [Fact]
    public async Task StateChangeDuringPruningPreventsClose()
    {
        var generation = 1;
        Assert.False(await CloseCommitBarrier.CompleteAsync(
            () => generation == 1,
            () => Task.CompletedTask,
            () => { generation++; return Task.CompletedTask; }));
    }

    [Fact]
    public async Task FailedStorageCannotApproveClose()
    {
        await Assert.ThrowsAsync<IOException>(() => CloseCommitBarrier.CompleteAsync(
            () => true,
            () => Task.FromException(new IOException("disk unavailable")),
            () => Task.CompletedTask));
    }
}
