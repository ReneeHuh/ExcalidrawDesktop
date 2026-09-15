namespace ExcalidrawDesktop.Core;

/// <summary>Remembers repair decisions for one window. Run requests in its modal queue.</summary>
public sealed class LibraryRepairSession
{
    private string? declinedRevision;

    public async Task<LibraryLoadResult> LoadAsync(
        Func<Task<LibraryLoadResult>> load,
        Func<string, Task<LibraryLoadResult>> repair,
        Func<Task<bool>> confirm,
        bool retry = false)
    {
        // Re-read after waiting for the modal queue: another window may have repaired it.
        var current = await load();
        if (current.Status != "corrupt") return current;
        if (!retry && declinedRevision == current.Revision) return Unavailable();
        if (!await confirm())
        {
            declinedRevision = current.Revision;
            return Unavailable();
        }
        declinedRevision = null;
        try { return await repair(current.Revision); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A stale approval or a failed write may still leave a valid shared library.
            var latest = await load();
            return latest.Status == "loaded" ? latest : Unavailable();
        }
    }

    private static LibraryLoadResult Unavailable() => new("unavailable", "[]", "");
}
