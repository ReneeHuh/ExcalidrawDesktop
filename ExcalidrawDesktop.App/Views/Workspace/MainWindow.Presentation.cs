using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace ExcalidrawDesktop.App;

// Native view wiring for this feature; document workflows stay in the controllers.
public sealed partial class MainWindow
{
    private void InitializePresentation()
    {
        MainLayout.DataContext = ViewModel;
        AppSettingsPage.Initialize(ViewModel.SettingsVM);
        // MenuBar creates a separate popup tree; its items do not inherit MainLayout's context.
        foreach (var item in FileMenu.Items)
        {
            item.DataContext = ViewModel;
        }
    }

    private void InitializeTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(WindowDragRegion);
#if DEBUG
        titleBarRegistered = true;
#endif

        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        ApplyTitleBarTheme();
    }

    private void OnTitleBarLoaded(object sender, RoutedEventArgs args)
    {
        if (!titleBarRootSubscribed && MainLayout.XamlRoot is { } xamlRoot)
        {
            titleBarRootSubscribed = true;
            titleBarXamlRoot = xamlRoot;
            xamlRoot.Changed += OnTitleBarXamlRootChanged;
        }
        UpdateTitleBarInsets();
    }

    private void OnTitleBarXamlRootChanged(
        XamlRoot sender,
        XamlRootChangedEventArgs args)
    {
        UpdateTitleBarInsets();
    }

    private void OnTitleBarSizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateTitleBarInsets();
    }

    private void UpdateTitleBarInsets()
    {
        var xamlRoot = MainLayout.XamlRoot;
        if (xamlRoot is null || xamlRoot.RasterizationScale <= 0)
        {
            return;
        }

        var scale = xamlRoot.RasterizationScale;
        TitleBarLeftInset.Width = new GridLength(AppWindow.TitleBar.LeftInset / scale);
        TitleBarRightInset.Width = new GridLength(AppWindow.TitleBar.RightInset / scale);
    }

    private void RestoreWindowPlacement()
    {
        var state = workspaceCoordinator.GetRestoreState(this);
        if (state?.Bounds is { Width: >= 320, Height: >= 240 } bounds)
        {
            var display = DisplayArea.GetFromRect(
                new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                DisplayAreaFallback.Primary);
            var workArea = display.WorkArea;
            PlaceWindow(WindowPlacement.ClampToWorkArea(
                bounds,
                new WorkspaceWindowBounds(workArea.X, workArea.Y, workArea.Width, workArea.Height)));
        }

        if (state?.IsMaximized == true &&
            AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    /// <summary>Moves and resizes the native window to physical-pixel bounds.</summary>
    internal void PlaceWindow(WorkspaceWindowBounds bounds) =>
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));

    private void UpdateTabHeader(DocumentSession session) => session.ViewModel.Refresh();

    private void UpdateWindowTitle()
    {
        var active = ActiveSession;
        UpdateFileMenuState(active);
        UpdateStatusBar(active);
        ViewModel.RefreshTitle(active, settingsPageVisible);
        Title = ViewModel.WindowTitle;
    }

    private void UpdateFileMenuState(DocumentSession? active) =>
        ViewModel.RefreshCommands(active, settingsPageVisible, sessions);

    private void UpdateStatusBar(DocumentSession? session)
    {
        if (!settingsPageVisible && session is not null && !ReferenceEquals(session, ActiveSession)) return;
        ViewModel.RefreshStatus(session, settingsPageVisible);
    }
}
