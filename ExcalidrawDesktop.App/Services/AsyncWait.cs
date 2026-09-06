namespace ExcalidrawDesktop.App.Services;

internal static class AsyncWait
{
    public static async Task<bool> UntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(pollInterval ?? TimeSpan.FromMilliseconds(50));
        }
        while (DateTimeOffset.UtcNow < deadline);
        return condition();
    }
}
