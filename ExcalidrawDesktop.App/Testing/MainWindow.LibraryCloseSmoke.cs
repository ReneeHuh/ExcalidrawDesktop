using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Services.Workspace;
using ExcalidrawDesktop.App.Sessions;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
#if DEBUG
    private async Task VerifyLibraryCloseCancellationAsync(DocumentSession session)
    {
        var itemId = "close-library-smoke-" + Guid.NewGuid().ToString("N");
        var path = Path.Combine(DesktopPaths.DataRoot, "library.json");
        Directory.CreateDirectory(DesktopPaths.DataRoot);
        if (!File.Exists(path)) await File.WriteAllTextAsync(path, "[]");
        var attributes = File.GetAttributes(path);
        var modals = WindowModalCoordinator.For(this);
        const string prefix = "(() => { const ownerWindow = document.getElementById('root').ownerDocument.defaultView; ";
        try
        {
            File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
            await session.CoreWebView!.ExecuteScriptAsync(prefix +
                $"ownerWindow.__EXCALIDRAW_DESKTOP_SMOKE__.addLibraryItem('{itemId}'); }})()");
            CheckCloseSmoke(await AsyncWait.UntilAsync(() => session.HasUnsavedLibrary, TimeSpan.FromSeconds(10)),
                "Library failure did not preserve pending changes.");
            modals.DialogForSmoke = _ => ContentDialogResult.None;
            CheckCloseSmoke(!await windowClose.RequestCloseSessionAsync(session) &&
                session.HasUnsavedLibrary && !session.DiscardLibraryOnClose && session.IsDirty && sessions.Contains(session),
                "Cancelling library close lost pending work.");
            modals.DialogForSmoke = _ => ContentDialogResult.Primary;
            CheckCloseSmoke(await windowClose.ResolveWindowCloseAsync() && session.DiscardLibraryOnClose,
                "Explicit library discard did not permit window close preparation.");
            // A later window can cancel Exit; release an earlier library decision
            // without throwing away its still-live changes.
            CancelPreparedClose();
            CheckCloseSmoke(session.HasUnsavedLibrary && !session.DiscardLibraryOnClose && session.IsDirty,
                "Aborting prepared close lost library or drawing changes.");
        }
        finally
        {
            modals.DialogForSmoke = null;
            File.SetAttributes(path, attributes);
            CancelPreparedClose();
        }
        await session.CoreWebView!.ExecuteScriptAsync(prefix + "ownerWindow.__EXCALIDRAW_DESKTOP_SMOKE__.retryLibrary(); })()");
        CheckCloseSmoke(await AsyncWait.UntilAsync(() => !session.HasUnsavedLibrary, TimeSpan.FromSeconds(15)) &&
            (await File.ReadAllTextAsync(path)).Contains(itemId, StringComparison.Ordinal),
            "Library edits could not be saved after cancelling close.");
    }
#endif
}
