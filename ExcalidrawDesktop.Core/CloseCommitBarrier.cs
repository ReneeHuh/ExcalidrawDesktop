namespace ExcalidrawDesktop.Core;

/// <summary>Never approve disposal using state captured before asynchronous storage work.</summary>
public static class CloseCommitBarrier
{
    public static async Task<bool> CompleteAsync(
        Func<bool> stateIsCurrent,
        Func<Task> persist,
        Func<Task> prune)
    {
        if (!stateIsCurrent())
        {
            return false;
        }
        await persist();
        if (!stateIsCurrent())
        {
            return false;
        }
        await prune();
        return stateIsCurrent();
    }
}
