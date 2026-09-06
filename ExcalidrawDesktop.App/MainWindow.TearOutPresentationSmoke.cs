using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
#if DEBUG
    private async Task RunUnavailableTabTearOutSmokeAsync(DocumentSession? session)
    {
        var windowCount = workspaceCoordinator.Windows.Count;
        var tabCount = sessions.Count;
        var editor = session?.CoreWebView;
        var destination = RequestTearOutWindow(session?.TabItem);
        if (destination.AppWindow.Id.Value == 0 || destination.IsClosed ||
            workspaceCoordinator.Windows.Count != windowCount)
        {
            throw new InvalidOperationException("An unavailable tab did not receive a valid isolated tear-out window.");
        }

        destination.AppWindow.Show(activateWindow: false);
        await Task.Delay(100);
        destination.AppWindow.Hide();
        if (TryCompletePendingTearOut(session?.TabItem) || destination.IsClosed ||
            !ReferenceEquals(pendingTearOutWindow, destination) ||
            sessions.Count != tabCount || workspaceCoordinator.Windows.Count != windowCount ||
            (session is not null && (!sessions.Contains(session) || !ReferenceEquals(session.CoreWebView, editor))))
        {
            throw new InvalidOperationException("A rejected tear-out moved its tab or destroyed the window still in use by WinUI.");
        }

        // WinUI continues using the destination after a rejected callback.
        destination.AppWindow.Show(activateWindow: false);
        destination.AppWindow.Hide();
        QueueDiscardPendingTearOutWindow();
        if (!await AsyncWait.UntilAsync(
                () => destination.IsClosed && pendingTearOutWindow is null,
                TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("A rejected tear-out did not clean up after the move loop.");
        }
    }

    private async Task PauseTearOutInteractionForSmokeAsync(DocumentSession initializingSession)
    {
        // Optional manual/native-input checkpoint: keep one editor initializing
        // while clicking and dragging its tab through WinUI's actual move loop.
        var requestPath = Path.Combine(AppContext.BaseDirectory, "tear-out-interaction-smoke.request");
        if (!File.Exists(requestPath)) return;

        DocumentTabs.SelectedItem = sessions.First(session => session != initializingSession).TabItem;
        Title = "Excalidraw Desktop — Tear-out interaction smoke ready";
        if (!await AsyncWait.UntilAsync(() => !File.Exists(requestPath), TimeSpan.FromMinutes(10)))
        {
            throw new TimeoutException("The tear-out interaction checkpoint was not released.");
        }
    }

    private async Task RunTearOutPresentationSmokeAsync(DocumentSession session)
    {
        var windowCount = workspaceCoordinator.Windows.Count;
        var sourcePosition = AppWindow.Position;
        var sourceSize = AppWindow.Size;

        // Settings has no document session but still enters the native callback.
        await RunUnavailableTabTearOutSmokeAsync(null);

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
