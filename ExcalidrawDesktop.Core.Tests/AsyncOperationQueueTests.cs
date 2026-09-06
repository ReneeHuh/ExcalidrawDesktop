using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Core.Tests;

public sealed class AsyncOperationQueueTests
{
    [Fact]
    public async Task SecondDialogWaitsForFirstAndFailureReleasesTheWindow()
    {
        var queue = new AsyncOperationQueue();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;
        var first = queue.RunAsync(async () =>
        {
            await release.Task;
            throw new IOException("picker failed");
        });
        var second = queue.RunAsync(() =>
        {
            secondStarted = true;
            return Task.FromResult(42);
        });
        Assert.True(queue.IsBusy);
        Assert.False(secondStarted);
        release.SetResult();
        await Assert.ThrowsAsync<IOException>(() => first);
        Assert.Equal(42, await second);
        Assert.False(queue.IsBusy);
    }
}
