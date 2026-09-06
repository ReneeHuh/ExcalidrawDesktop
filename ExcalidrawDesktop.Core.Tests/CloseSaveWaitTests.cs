namespace ExcalidrawDesktop.Core.Tests;

public sealed class CloseSaveWaitTests
{
    [Fact]
    public async Task SilentEditorTimesOut()
    {
        var response = new TaskCompletionSource<bool>();
        Assert.False(await CloseSaveWait.UntilAsync(response.Task,
            TimeSpan.FromMilliseconds(10), () => false));
        Assert.False(response.Task.IsCompleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedResponseWinsOverExpiredDeadline(bool result)
    {
        Assert.Equal(result, await CloseSaveWait.UntilAsync(Task.FromResult(result),
            TimeSpan.Zero, () => false));
    }

    [Fact]
    public async Task PickerPausesDeadlineAndClosingItResumesTimeout()
    {
        var response = new TaskCompletionSource<bool>();
        var pickerOpen = true;
        var wait = CloseSaveWait.UntilAsync(response.Task, TimeSpan.Zero, () => pickerOpen);
        Assert.False(wait.IsCompleted);
        pickerOpen = false;
        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CompletionReleasesWaitWhilePickerIsOpen()
    {
        var response = new TaskCompletionSource<bool>();
        var wait = CloseSaveWait.UntilAsync(response.Task, TimeSpan.Zero, () => true);
        response.SetResult(true);
        Assert.True(await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CancellationReleasesPausedWait()
    {
        using var cancellation = new CancellationTokenSource();
        var wait = CloseSaveWait.UntilAsync(new TaskCompletionSource<bool>().Task,
            TimeSpan.Zero, () => true, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}
