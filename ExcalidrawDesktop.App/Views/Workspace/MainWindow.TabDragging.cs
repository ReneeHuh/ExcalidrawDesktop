using ExcalidrawDesktop.App.Services.Logging;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.App.Views.Workspace;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WinRT.Interop;

namespace ExcalidrawDesktop.App;

// Tab drag-and-drop: reordering within the strip, drops onto another window's
// strip, and drops outside every strip that open a new window. WinUI's native
// tear-out is not used because it creates a hidden window on every tab click.
public sealed partial class MainWindow
{
    private const string DraggedTabProperty = "ExcalidrawDesktop.DocumentTab";
    private DesktopDragInput? tabDragInput;

    private void OnTabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        if (!CanStartTabDrag(FindSession(args.Tab), CommandsBlocked))
        {
            args.Cancel = true;
            return;
        }

        args.Data.Properties[DraggedTabProperty] = args.Tab;
        args.Data.RequestedOperation = DataPackageOperation.Move;
        tabDragInput?.Dispose();
        tabDragInput = DesktopDragInput.TryStart();
    }

    /// <summary>
    /// Every drawing tab can start a drag, including sleeping and not yet
    /// initialized ones, so reordering always works. The stricter
    /// CanMoveSession check applies only where a drop would leave the window.
    /// Non-document tabs such as Settings never start a drag.
    /// </summary>
    private static bool CanStartTabDrag(DocumentSession? session, bool commandsBlocked) =>
        !commandsBlocked && session is { IsMoving: false };

    private void OnTabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        var pointer = tabDragInput?.State.ReleasePoint;
        tabDragInput?.Dispose();
        tabDragInput = null;

        TryQueueOutsideTabDrop(args.Tab, args.DropResult, pointer);
    }

    private bool TryQueueOutsideTabDrop(TabViewItem? tab, DataPackageOperation result, PointInt32? pointer)
    {
        // WinUI reports None for both outside drops and cancellation. Require
        // an actual release, and do not reinterpret a refused strip as outside.
        if (tab is null || result != DataPackageOperation.None || pointer is not { } point ||
            workspaceCoordinator.Windows.Any(window => window.IsPointOverTabStrip(point)))
        {
            return false;
        }

        // Finish WinUI's drag callback before reparenting the live WebView.
        return DispatcherQueue.TryEnqueue(() => TryMoveDroppedTabToNewWindow(tab, point));
    }

    private bool IsPointOverTabStrip(PointInt32 point)
    {
        if (IsClosed || !AppWindow.IsVisible ||
            AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } ||
            MainLayout.XamlRoot is not { } root ||
            DesktopPointer.TryGetClientPosition(WindowNative.GetWindowHandle(this), point) is not { } client)
        {
            return false;
        }

        // Include the strip's header, footer, and caption buttons, even while a
        // dialog prevents XAML hit testing. Coordinates are physical pixels.
        return client.X >= 0 && client.X < AppWindow.ClientSize.Width &&
            client.Y >= 0 && client.Y < TitleBarContainer.ActualHeight * root.RasterizationScale;
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

        LogAction("Tab dropped into window", "drag", session);
        try
        {
            var index = TabStripGeometry.GetDropIndex(DocumentTabs, args.GetPosition(DocumentTabs).X);
            workspaceCoordinator.MoveSession(source, session, this, index);
        }
        catch (Exception exception)
        {
            AppLogger.Error("[MainWindow] Tab drop between windows failed", exception);
        }
    }
}
