namespace ExcalidrawDesktop.Core;

/// <summary>Serializes modal operations, including their asynchronous lifetime.</summary>
public sealed class AsyncOperationQueue
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private int pending;

    public bool IsBusy => Volatile.Read(ref pending) != 0;

    public async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        Interlocked.Increment(ref pending);
        await gate.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            Interlocked.Decrement(ref pending);
            gate.Release();
        }
    }

    public Task RunAsync(Func<Task> operation) => RunAsync(async () =>
    {
        await operation();
        return true;
    });
}
