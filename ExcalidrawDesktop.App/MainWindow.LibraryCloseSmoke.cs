using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
#if DEBUG
    private async Task VerifyLibraryDiscardCloseAsync()
    {
        // The preceding Save All decision intentionally leaves its barriers held.
        ExitCloseBarrier(sessions.ToArray());
        var session = sessions[0];
        var itemId = "discard-library-smoke-" + Guid.NewGuid().ToString("N");
        var path = Path.Combine(DesktopPaths.DataRoot, "library.json");
        var attributes = File.GetAttributes(path);
        try
        {
            File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
            await session.CoreWebView!.ExecuteScriptAsync(
                $"window.__EXCALIDRAW_DESKTOP_SMOKE__.addLibraryItem('{itemId}')");
            if (!await AsyncWait.UntilAsync(() => session.HasUnsavedLibrary, TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException("The library discard fixture did not become pending.");
            Title = "Excalidraw Desktop — Library close smoke: discard and close window";
            if (!await windowClose.ResolveWindowCloseAsync() || !session.DiscardLibraryOnClose)
                throw new InvalidOperationException("Library discard did not permit the window to close.");
        }
        finally { File.SetAttributes(path, attributes); }
    }

    private async Task VerifyLibraryCloseCancellationAsync(DocumentSession session)
    {
        var itemId = "close-library-smoke-" + Guid.NewGuid().ToString("N");
        var path = Path.Combine(DesktopPaths.DataRoot, "library.json");
        Directory.CreateDirectory(DesktopPaths.DataRoot);
        if (!File.Exists(path)) await File.WriteAllTextAsync(path, "[]");
        var attributes = File.GetAttributes(path);
        try
        {
            File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
            await session.CoreWebView!.ExecuteScriptAsync(
                $"window.__EXCALIDRAW_DESKTOP_SMOKE__.addLibraryItem('{itemId}')");
            if (!await AsyncWait.UntilAsync(() => session.HasUnsavedLibrary, TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException("Library failure did not preserve pending changes.");

            Title = "Excalidraw Desktop — Library close smoke: choose Cancel";
            if (await windowClose.RequestCloseSessionAsync(session) || !session.HasUnsavedLibrary ||
                session.DiscardLibraryOnClose || !session.IsDirty || !sessions.Contains(session))
                throw new InvalidOperationException("Cancelling library close lost pending work.");

            Title = "Excalidraw Desktop — Library close smoke: discard library then cancel drawing";
            if (await windowClose.ResolveWindowCloseAsync() || !session.HasUnsavedLibrary ||
                session.DiscardLibraryOnClose || !session.IsDirty || !sessions.Contains(session))
                throw new InvalidOperationException("Cancelling drawing close lost the library or its next close decision.");
        }
        finally { File.SetAttributes(path, attributes); }

        await session.CoreWebView!.ExecuteScriptAsync("window.__EXCALIDRAW_DESKTOP_SMOKE__.retryLibrary()");
        if (!await AsyncWait.UntilAsync(() => !session.HasUnsavedLibrary, TimeSpan.FromSeconds(15)) ||
            !(await File.ReadAllTextAsync(path)).Contains(itemId, StringComparison.Ordinal))
            throw new InvalidOperationException("Library edits could not be saved after cancelling close.");
    }
#endif
}
