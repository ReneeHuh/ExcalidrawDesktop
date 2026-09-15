using System.Diagnostics;

namespace ExcalidrawDesktop.Core;

/// <summary>Bounds an editor response wait without timing out a native file picker.</summary>
public static class CloseSaveWait
{
    public static async Task<bool> UntilAsync(
        Task<bool> response,
        TimeSpan timeout,
        Func<bool> isPickerOpen,
        CancellationToken cancellationToken = default)
    {
        var remaining = timeout;
        while (!response.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paused = isPickerOpen();
            if (!paused && remaining <= TimeSpan.Zero)
            {
                return false;
            }

            var start = Stopwatch.GetTimestamp();
            using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(
                paused || remaining > TimeSpan.FromMilliseconds(100)
                    ? TimeSpan.FromMilliseconds(100) : remaining,
                delayCancellation.Token);
            if (await Task.WhenAny(response, delay) == response)
            {
                delayCancellation.Cancel();
                break;
            }
            await delay;
            // Ignore the boundary interval when a picker opens or closes.
            if (!paused && !isPickerOpen())
            {
                remaining -= Stopwatch.GetElapsedTime(start);
            }
        }
        return await response;
    }
}
