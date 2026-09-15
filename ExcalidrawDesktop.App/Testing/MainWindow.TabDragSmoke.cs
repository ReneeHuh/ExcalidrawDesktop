using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Sessions;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace ExcalidrawDesktop.App;

// Debug smoke checks for tab sizing and tab drag-and-drop between windows.
public sealed partial class MainWindow
{
#if DEBUG
    private Task RunUnavailableTabDropSmokeAsync(DocumentSession? session)
    {
        var windowCount = workspaceCoordinator.Windows.Count;
        var tabCount = sessions.Count;
        var editor = session?.CoreWebView;
        if (TryMoveDroppedTabToNewWindow(session?.TabItem) ||
            sessions.Count != tabCount || workspaceCoordinator.Windows.Count != windowCount ||
            (session is not null && (!sessions.Contains(session) || !ReferenceEquals(session.CoreWebView, editor))))
        {
            throw new InvalidOperationException("An unavailable tab created a window or moved its editor.");
        }
        return Task.CompletedTask;
    }

    private async Task PauseTabDragInteractionForSmokeAsync(DocumentSession initializingSession)
    {
        // Optional manual checkpoint: keep one editor initializing while a
        // person drags its tab with real input.
        var requestPath = Path.Combine(AppContext.BaseDirectory, "tab-drag-interaction-smoke.request");
        if (!File.Exists(requestPath)) return;

        DocumentTabs.SelectedItem = sessions.First(session => session != initializingSession).TabItem;
        Title = "Excalidraw Desktop — Tab drag interaction smoke ready";
        if (!await AsyncWait.UntilAsync(() => !File.Exists(requestPath), TimeSpan.FromMinutes(10)))
        {
            throw new TimeoutException("The tab drag interaction checkpoint was not released.");
        }
    }

    private async Task RunTabDragSmokeAsync(DocumentSession session)
    {
        var windowCount = workspaceCoordinator.Windows.Count;
        var sourcePosition = AppWindow.Position;
        var sourceSize = AppWindow.Size;
        await RunUnavailableTabDropSmokeAsync(null);

        if (DocumentTabs.CanTearOutTabs || !DocumentTabs.CanDragTabs || !DocumentTabs.CanReorderTabs)
            throw new InvalidOperationException("Tabs must use drag-and-drop without native placeholder windows.");

        foreach (var tab in DocumentTabs.TabItems.OfType<TabViewItem>().ToArray())
        {
            DocumentTabs.SelectedItem = tab;
            await Task.Delay(100);
            if (workspaceCoordinator.Windows.Count != windowCount || !AppWindow.IsVisible ||
                AppWindow.Position != sourcePosition || AppWindow.Size != sourceSize)
                throw new InvalidOperationException("Selecting a tab changed the window.");
        }
        DocumentTabs.SelectedItem = session.TabItem;
        await Task.Delay(100);
        var widths = DocumentTabs.TabItems.OfType<TabViewItem>()
            .Select(tab => tab.ActualWidth).ToArray();
        if (widths.Any(width => width < TabMinWidth - 1 || width > TabMaxWidth + 1) ||
            widths.Max() - widths.Min() > 1)
            throw new InvalidOperationException($"Tab widths are inconsistent: {string.Join(", ", widths)}");

        var extraTabs = new List<DocumentSession>();
        try
        {
            // Fill the strip without initializing background editors. Verify
            // actual layout, including the newly inserted tabs.
            for (var i = 0; i < 12; i++) extraTabs.Add(CreateTab(select: false));
            await Task.Delay(200);
            var crowdedWidths = DocumentTabs.TabItems.OfType<TabViewItem>()
                .Select(tab => tab.ActualWidth).ToArray();
            if (crowdedWidths.Any(width => width < TabMinWidth - 1 || width >= widths[0] - 1) ||
                crowdedWidths.Max() - crowdedWidths.Min() > 1)
                throw new InvalidOperationException($"Crowded tabs did not shrink evenly: {string.Join(", ", crowdedWidths)}");
            // Tabs whose editors have not started, and sleeping tabs, can start
            // a drag for reordering even though neither may leave the window.
            if (extraTabs.Any(extra =>
                    extra.IsReady || !CanStartTabDrag(extra, CommandsBlocked) || CanMoveSession(extra)))
                throw new InvalidOperationException("Uninitialized tabs must stay draggable but not movable.");
            var sleeping = extraTabs[0];
            sleeping.IsSuspended = true;
            try
            {
                if (!CanStartTabDrag(sleeping, CommandsBlocked) || CanMoveSession(sleeping))
                    throw new InvalidOperationException("Sleeping tabs must stay draggable but not movable.");
            }
            finally
            {
                sleeping.IsSuspended = false;
            }
            if (CanStartTabDrag(null, false) || CanStartTabDrag(extraTabs[0], true))
                throw new InvalidOperationException("Non-document tabs and blocked windows must not start a drag.");
        }
        finally
        {
            foreach (var extra in extraTabs) CloseSession(extra);
        }
        await Task.Delay(200);
        if (Math.Abs(session.TabItem.ActualWidth - widths[0]) > 1)
            throw new InvalidOperationException("Tabs did not expand after closing the extra tabs.");

        var editor = session.CoreWebView;
        var pointer = new PointInt32(
            sourcePosition.X + sourceSize.Width / 2,
            sourcePosition.Y + sourceSize.Height / 2);
        var expected = GetDroppedTabWindowBounds(pointer);
        if (!TryMoveDroppedTabToNewWindow(session.TabItem, pointer))
            throw new InvalidOperationException("Dropping a live tab outside did not create its window.");
        var destination = workspaceCoordinator.FindSessionByTab(session.TabItem)!.Value.Window;
        if (!destination.AppWindow.IsVisible || !ReferenceEquals(session.CoreWebView, editor))
            throw new InvalidOperationException("The dropped tab lost its live editor.");
        var placed = destination.AppWindow.Position;
        var placedSize = destination.AppWindow.Size;
        if (Math.Abs(placed.X - expected.X) > 2 || Math.Abs(placed.Y - expected.Y) > 2 ||
            Math.Abs(placedSize.Width - expected.Width) > 2 || Math.Abs(placedSize.Height - expected.Height) > 2 ||
            pointer.X < placed.X || pointer.X > placed.X + placedSize.Width ||
            pointer.Y < placed.Y || pointer.Y > placed.Y + placedSize.Height / 4)
            throw new InvalidOperationException(
                $"The dropped tab opened its window at {placed.X},{placed.Y} {placedSize.Width}x{placedSize.Height} " +
                $"instead of {expected.X},{expected.Y} {expected.Width}x{expected.Height} for pointer {pointer.X},{pointer.Y}.");
        if (!workspaceCoordinator.MoveSession(destination, session, this, 0) ||
            !ReferenceEquals(session.CoreWebView, editor) || workspaceCoordinator.Windows.Count != windowCount)
            throw new InvalidOperationException("Moving a tab back lost the editor or leaked a window.");
        await Task.Delay(200);
    }
#endif
}
