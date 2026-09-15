using ExcalidrawDesktop.App.Services.Logging;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace ExcalidrawDesktop.App;

// Tab drag-and-drop: reordering within the strip, drops onto another window's
// strip, and drops outside every strip that open a new window. WinUI's native
// tear-out is not used because it creates a hidden window on every tab click.
public sealed partial class MainWindow
{
    private const string DraggedTabProperty = "ExcalidrawDesktop.DocumentTab";

    private void OnTabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        if (!CanStartTabDrag(FindSession(args.Tab), CommandsBlocked))
        {
            args.Cancel = true;
            return;
        }

        args.Data.Properties[DraggedTabProperty] = args.Tab;
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    /// <summary>
    /// Every drawing tab can start a drag, including sleeping and not yet
    /// initialized ones, so reordering always works. The stricter
    /// CanMoveSession check applies only where a drop would leave the window.
    /// Non-document tabs such as Settings never start a drag.
    /// </summary>
    private static bool CanStartTabDrag(DocumentSession? session, bool commandsBlocked) =>
        !commandsBlocked && session is { IsMoving: false };

    private void OnTabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args)
    {
        // Read the pointer now; the drop position is gone once this returns.
        // Finish the drag callback before reparenting the live WebView.
        // Ordinary clicks and in-strip reordering never reach this handler.
        var tab = args.Tab;
        var pointer = DesktopPointer.TryGetScreenPosition();
        DispatcherQueue.TryEnqueue(() => TryMoveDroppedTabToNewWindow(tab, pointer));
    }

    private bool TryMoveDroppedTabToNewWindow(TabViewItem? tab, PointInt32? pointer = null)
    {
        if (IsClosed || tab is null || FindSession(tab) is not { } session || !CanMoveSession(session))
        {
            return false;
        }

        try
        {
            LogAction("Tab dropped outside strip", "drag", session);
            var placement = pointer is { } point ? GetDroppedTabWindowBounds(point) : null;
            return workspaceCoordinator.MoveSessionToNewWindow(this, session, placement);
        }
        catch (Exception exception)
        {
            AppLogger.Error("[MainWindow] Tab drop into new window failed", exception);
            return false;
        }
    }

    /// <summary>
    /// Bounds for the window a dropped tab opens: this window's size, placed so
    /// the pointer rests on the new window's first tab.
    /// </summary>
    internal WorkspaceWindowBounds GetDroppedTabWindowBounds(PointInt32 pointer)
    {
        var workArea = DisplayArea.GetFromPoint(pointer, DisplayAreaFallback.Nearest).WorkArea;
        var scale = MainLayout.XamlRoot?.RasterizationScale ?? 1;
        var headerWidth = (DocumentTabs.TabStripHeader as FrameworkElement)?.ActualWidth ?? 0;
        var size = AppWindow.Size;
        return WindowPlacement.ForDroppedTab(
            pointer.X,
            pointer.Y,
            size.Width,
            size.Height,
            sourceMaximized: AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized },
            pointerInsetX: (int)Math.Round((headerWidth + TabMaxWidth / 2) * scale),
            pointerInsetY: (int)Math.Round(TitleBarContainer.Height / 2 * scale),
            new WorkspaceWindowBounds(workArea.X, workArea.Y, workArea.Width, workArea.Height));
    }

    private static TabViewItem? GetDraggedTab(DragEventArgs args) =>
        args.DataView.Properties.TryGetValue(DraggedTabProperty, out var value)
            ? value as TabViewItem
            : null;

    private void OnTabStripDragOver(object sender, DragEventArgs args)
    {
        if (GetDraggedTab(args) is not { } tab)
        {
            return;
        }

        // Mark handled: MainLayout's file-drop handler would otherwise reset
        // the accepted operation to None for anything that is not a file.
        args.Handled = true;
        args.AcceptedOperation = CanAcceptDraggedTab(tab)
            ? DataPackageOperation.Move
            : DataPackageOperation.None;
    }

    private bool CanAcceptDraggedTab(TabViewItem tab)
    {
        if (workspaceCoordinator.FindSessionByTab(tab) is not { Window: var source, Session: var session })
        {
            return false;
        }

        // The TabView performs in-strip reordering itself.
        return ReferenceEquals(source, this)
            ? !CommandsBlocked
            : CanReceiveSession() && source.CanMoveSession(session);
    }

    private void OnTabStripDrop(object sender, DragEventArgs args)
    {
        if (GetDraggedTab(args) is not { } tab ||
            workspaceCoordinator.FindSessionByTab(tab) is not { Window: var source, Session: var session } ||
            ReferenceEquals(source, this))
        {
            return;
        }

        args.Handled = true;
        if (!CanReceiveSession() || !source.CanMoveSession(session))
        {
            return;
        }

        var pointerX = args.GetPosition(DocumentTabs).X;
        var slots = DocumentTabs.TabItems
            .OfType<TabViewItem>()
            .Select(item => new TabStripSlot(
                item.TransformToVisual(DocumentTabs).TransformPoint(new Windows.Foundation.Point()).X,
                item.ActualWidth))
            .ToArray();
        var index = TabStripDropIndex.FromPointer(pointerX, slots);
        LogAction("Tab dropped into window", "drag", session);
        try
        {
            workspaceCoordinator.MoveSession(source, session, this, index);
        }
        catch (Exception exception)
        {
            AppLogger.Error("[MainWindow] Tab drop between windows failed", exception);
        }
    }
}
