using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
#if DEBUG
    private async Task RunTearOutPresentationSmokeAsync(DocumentSession session)
    {
        var windowCount = workspaceCoordinator.Windows.Count;
        var sourcePosition = AppWindow.Position;
        var sourceSize = AppWindow.Size;

        // Reproduce WinUI's preparatory show/hide and close the unused window
        // as after a click without tearing out. The destination must remain
        // available to WinUI's move loop without appearing in the taskbar.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var placeholder = PreparePendingTearOutWindow();
            placeholder.AppWindow.Show(activateWindow: false);
            await Task.Delay(100);
            if (!placeholder.AppWindow.IsVisible ||
                placeholder.AppWindow.IsShownInSwitchers ||
                workspaceCoordinator.Windows.Count != windowCount)
            {
                throw new InvalidOperationException(
                    "The tear-out preparation could not render or appeared in the taskbar.");
            }
            placeholder.AppWindow.Hide();
            placeholder.Close();
            if (!await AsyncWait.UntilAsync(
                    () => placeholder.IsClosed && pendingTearOutWindow is null,
                    TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException(
                    "A cancelled tear-out retained its placeholder window.");
            }
        }

        if (AppWindow.Position != sourcePosition || AppWindow.Size != sourceSize)
        {
            throw new InvalidOperationException(
                "Preparing a tear-out changed the source window's bounds.");
        }

        var editor = session.CoreWebView;
        var destination = PreparePendingTearOutWindow();
        // Mimic the same native initialization before a real transfer.
        destination.AppWindow.Show(activateWindow: false);
        await Task.Delay(100);
        destination.AppWindow.Hide();
        if (!workspaceCoordinator.MoveSession(this, session, destination))
        {
            throw new InvalidOperationException("The prepared window rejected a live tab.");
        }
        await Task.Delay(100);
        if (!destination.AppWindow.IsVisible ||
            !destination.AppWindow.IsShownInSwitchers ||
            !ReferenceEquals(session.CoreWebView, editor) ||
            !destination.OpenSessions.Contains(session))
        {
            throw new InvalidOperationException(
                "The torn-out window did not reveal its live editor.");
        }
        if (!workspaceCoordinator.MoveSession(destination, session, this, 0) ||
            !ReferenceEquals(session.CoreWebView, editor) ||
            workspaceCoordinator.Windows.Count != windowCount)
        {
            throw new InvalidOperationException(
                "Moving back from a prepared window lost the editor or leaked a window.");
        }
        // Let queued WebView title/state notifications settle before the caller
        // publishes the smoke-test completion title.
        await Task.Delay(200);
    }
#endif
}
