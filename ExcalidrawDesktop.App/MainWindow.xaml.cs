using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services.Documents;
using ExcalidrawDesktop.App.Services.Editor;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Services.Workspace;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.App.Testing;
using ExcalidrawDesktop.App.ViewModels;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    private const string WebView2HelpUri =
        "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";
    private static readonly TimeSpan ExitActivationTimeout =
        TimeSpan.FromSeconds(3);

    private readonly string webAssetPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Web");
    private readonly ApplicationWorkspaceCoordinator workspaceCoordinator;
    private readonly EditorSessionController editorSessions;
    private readonly DocumentLifecycleController documents;
    private readonly WindowCloseController windowClose;
    private readonly bool restoreWorkspace;
    private readonly List<DocumentSession> sessions = [];
    private readonly List<string> recentFiles;
    private readonly Queue<string> pendingActivatedFiles = [];
    private readonly DispatcherTimer suspensionTimer = new();
    private readonly ImageExportController imageExports;
    private readonly RecoverySnapshotStore recoverySnapshotStore;
    private readonly DesktopSmokeOptions smoke;
    private DesktopPreferences desktopPreferences = DesktopPreferences.Default;
    private DocumentSession? lastDocumentSession;
    private TabViewItem? settingsTabItem;
    private bool allowClose;
    private bool isWindowReady;
    private bool isWindowActive;
#if DEBUG
    private bool titleBarRegistered;
#endif
    private bool titleBarRootSubscribed;
    private MainWindow? pendingTearOutWindow;
    private bool settingsPageVisible;
    private bool windowClosePromptOpen;
    private bool openingActivatedFiles;
    private bool jumpListUpdateQueued;
    private bool jumpListUpdateRunning;
    private bool restoringWorkspace = true;
    private bool resourcesDisposed;
    private int untitledSequence;
    private XamlRoot? titleBarXamlRoot;

    internal MainWindow(
        ApplicationWorkspaceCoordinator workspaceCoordinator,
        bool restoreWorkspace = true,
        bool createInitialTab = true,
        DesktopSmokeOptions? smokeOptions = null)
    {
        smoke = smokeOptions ?? DesktopSmokeOptions.None;
        InitializeComponent();
        InitializePresentation();
        Title = DesktopResources.Get(
            "StartingWindowTitle",
            "Excalidraw Desktop — Starting…");
        this.workspaceCoordinator = workspaceCoordinator;
        documents = new DocumentLifecycleController(this, sessions, DocumentTabs, workspaceCoordinator);
        windowClose = new WindowCloseController(this, sessions, DocumentTabs);
        imageExports = new ImageExportController(this, sessions, documents);
#if DEBUG
        documents.SnapshotSavedForSmoke = OnRecoverySnapshotSavedForSmokeAsync;
        imageExports.ExportCompletedForSmoke = OnImageExportCompletedForSmoke;
        imageExports.ExportFailedForSmoke = OnImageExportFailedForSmoke;
#endif
        editorSessions = new EditorSessionController(
            sessions,
            () => ActiveSession,
            webAssetPath,
            OpenExternalUriAsync,
            smoke.RequiresEditorSmokeApi,
            workspaceCoordinator.RecoverySnapshotStore);
        editorSessions.StateChanged += OnEditorStateChanged;
        editorSessions.CommandsBlocked = () => CommandsBlocked;
        editorSessions.LibraryRequest = HandleLibraryRequestAsync;
        workspaceCoordinator.LibraryChanged += OnSharedLibraryChanged;
        editorSessions.Ready += OnAppReady;
        editorSessions.Resumed += OnEditorResumed;
        editorSessions.Failed += OnEditorFailed;
        editorSessions.TitleChanged += (session, title) =>
        {
            if (ReferenceEquals(session, ActiveSession)) Title = ViewModel.WindowTitle = title;
        };
        editorSessions.CloseBarrierReady += OnDirtyChanged;
        editorSessions.DocumentLoadApplied += OnDocumentLoadApplied;
        documents.DocumentLoadTimedOut += session => editorSessions.HandleLoadFailure(session);
        editorSessions.EditorRecreated += ConfigureEditorDropTarget;
        editorSessions.ImageExportRequested += imageExports.OnImageExportWebResourceRequested;
#if DEBUG
        editorSessions.BeforeInitializeForSmoke = () =>
            smoke.RunMultiWindowSmoke && multiWindowInitializationRelease is { } release
                ? release.Task
                : Task.CompletedTask;
#endif
        recentFiles = workspaceCoordinator.RecentFiles;
        this.restoreWorkspace = restoreWorkspace;
        desktopPreferences = workspaceCoordinator.Preferences;
        MainLayout.FlowDirection = workspaceCoordinator.EffectiveLanguage.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        ViewModel.SettingsVM.LoadPreferences(
            desktopPreferences,
            workspaceCoordinator.EffectiveLanguage,
            workspaceCoordinator.StartupLanguagePreference);
        ViewModel.SettingsVM.PreferencesChanged += OnPreferencesChanged;
        ViewModel.SettingsVM.SettingsPersistenceRetryRequested += OnSettingsPersistenceRetry;
        MainLayout.ActualThemeChanged += OnActualThemeChanged;
        ApplyDesktopTheme();
        ToolTipService.SetToolTip(
            SettingsButton,
            DesktopResources.Get("SettingsButtonToolTip", "Show Settings tab"));
        InitializeTitleBar();
        InitializeTearOutLifetime();
        // Windows share the coordinator's store so snapshot writes and the
        // coordinator's pruning always target the same directory.
        recoverySnapshotStore = workspaceCoordinator.RecoverySnapshotStore;
        PopulateRecentFilesMenu();
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        DocumentTabs.TabItemsChanged += OnTabItemsChanged;
        suspensionTimer.Interval = TimeSpan.FromSeconds(30);
        suspensionTimer.Tick += OnSuspensionTimerTick;
        suspensionTimer.Start();
        if (createInitialTab)
        {
            CreateTab();
        }
        if (smoke.RunTabSmoke)
        {
            CreateTab();
        }
        if (smoke.RunRecoverySmoke)
        {
            CreateTab();
        }
        if (smoke.RunTitleBarSmoke)
        {
            CreateTab();
        }
        if (smoke.RunMultiWindowSmoke)
        {
            CreateTab();
        }
        if (smoke.RunSuspensionSmoke)
        {
            CreateTab();
            CreateTab();
        }
        if (smoke.RunDocumentSafetySmoke)
        {
            CreateTab();
        }
        if (smoke.RunCloseDecisionsSmoke)
        {
            CreateTab();
        }
        for (var index = 1; index < smoke.PerformanceTabCount; index++)
        {
            CreateTab();
        }
    }

    private async void OnMainLayoutLoaded(object sender, RoutedEventArgs args)
    {
        RestoreWindowPlacement();
        if (restoreWorkspace)
        {
            await RestoreWorkspaceAsync();
        }
        else
        {
            restoringWorkspace = false;
            PopulateRecentFilesMenu();
        }
        isWindowReady = true;
        await workspaceCoordinator.NotifyWindowReadyAsync(this);
#if DEBUG
        TryRunMultiWindowExitSmoke();
#endif
        if (ActiveSession is { } session)
        {
            _ = editorSessions.InitializeAsync(session);
        }
        _ = DrainActivatedFilesAsync();
    }

    internal IReadOnlyList<DocumentSession> OpenSessions => sessions;

    internal bool IsReadyForActivation => isWindowReady && !resourcesDisposed;

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        isWindowActive =
            args.WindowActivationState != WindowActivationState.Deactivated;
        TitleBarContainer.Opacity =
            !isWindowActive
                ? 0.82
                : 1;
        UpdateTitleBarInsets();

        if (isWindowActive)
        {
            workspaceCoordinator.NotifyWindowActivated(this);
        }

        if (isWindowReady &&
            !settingsPageVisible &&
            isWindowActive &&
            ActiveSession is { } session)
        {
            if (session.IsUnloaded)
            {
                _ = editorSessions.WakeAsync(session);
            }
            else
            {
                _ = editorSessions.InitializeAsync(session);
            }
            _ = documents.CheckExternalFileStateAsync(session, showPrompt: true);
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange)
        {
            QueuePersistWorkspace();
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (resourcesDisposed)
        {
            return;
        }

        resourcesDisposed = true;
        workspaceCoordinator.UnregisterWindow(this);
        suspensionTimer.Stop();
        suspensionTimer.Tick -= OnSuspensionTimerTick;
        Activated -= OnWindowActivated;
        Closed -= OnWindowClosed;
        if (tearOutPointerSource is not null)
        {
            tearOutPointerSource.ExitedMoveSize -= OnTearOutMoveSizeExited;
            tearOutPointerSource = null;
        }
        workspaceCoordinator.LibraryChanged -= OnSharedLibraryChanged;
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        DocumentTabs.TabItemsChanged -= OnTabItemsChanged;
        pendingTearOutWindow?.CloseIfEmptyAfterMove();
        pendingTearOutWindow = null;
        ViewModel.SettingsVM.PreferencesChanged -= OnPreferencesChanged;
        ViewModel.SettingsVM.SettingsPersistenceRetryRequested -= OnSettingsPersistenceRetry;
        MainLayout.ActualThemeChanged -= OnActualThemeChanged;
        if (titleBarXamlRoot is { } xamlRoot)
        {
            xamlRoot.Changed -= OnTitleBarXamlRootChanged;
            titleBarXamlRoot = null;
        }

        foreach (var session in sessions.ToArray())
        {
            session.Dispose();
        }
        sessions.Clear();
        ViewModel.RefreshStatus(null, settingsPageVisible: false);
        ViewModel.RefreshCommands(null, settingsPageVisible: false, sessions);
    }

    private void OnSuspensionTimerTick(object? sender, object args)
    {
        var now = DateTimeOffset.UtcNow;
        var unloadCutoff = now - TimeSpan.FromMinutes(15);
        foreach (var session in sessions.Where(session =>
            desktopPreferences.UnloadInactiveTabs &&
            session.InactiveSince <= unloadCutoff && editorSessions.CanUnload(session)).ToArray())
        {
            _ = editorSessions.UnloadAsync(session, requireIdle: true);
        }

        var cutoff = now - TimeSpan.FromMinutes(5);
        foreach (var session in sessions.Where(session =>
            desktopPreferences.SuspendInactiveTabs &&
            session.InactiveSince <= cutoff && editorSessions.CanSuspend(session)).ToArray())
        {
            _ = editorSessions.SuspendAsync(session, requireIdle: true);
        }
    }

    private void OnEditorStateChanged(DocumentSession session)
    {
        UpdateTabHeader(session);
        UpdateStatusBar(session);
        UpdateFileMenuState(ActiveSession);
    }

    private void OnEditorResumed(DocumentSession session)
    {
        SendEditorTheme(session);
        SendEditorLanguage(session);
    }

    private void OnEditorFailed(DocumentSession session)
    {
        session.Dispatcher.CancelPendingSave();
        windowClose.OnCloseCancelled(session);
        if (session.PendingImageExport is { } pendingExport)
        {
            _ = imageExports.FailImageExportAsync(
                session,
                pendingExport.ExportId,
                DesktopResources.Get(
                    "EditorStoppedDuringExport",
                    "The editor stopped while creating the PNG."));
        }
    }

    private void OnAppReady(DocumentSession session)
    {
        SendEditorTheme(session);
        SendEditorLanguage(session);
        UpdateWindowTitle();
        documents.SendPendingDocumentLoad(session);
        if (session.IsRestoringFromHibernation &&
            session.PendingDocumentLoad is null)
        {
            editorSessions.CompleteHibernationRestore(session);
        }
        TryRunTabSmoke();
#if DEBUG
        TryRunTitleBarSmoke();
        TryRunMultiWindowSmoke();
        TryRunMultiWindowExitSmoke();
        TryRunPerformanceSmoke();
        TryRunSuspensionSmoke();
        TryRunImageExportSmoke();
        TryRunDocumentSafetySmoke();
        TryRunCloseDecisionsSmoke();
#endif
#if DEBUG
        TryRunRecoverySnapshotSmoke();
#endif
    }

    private void LogAction(
        string action,
        string inputSource,
        DocumentSession? targetSession = null)
    {
        var windowId = workspaceCoordinator.TryGetLogicalWindowId(this, out var id)
            ? id
            : null;
        var active = ActiveSession;
        var target = targetSession ?? active;
        DiagnosticLogService.Info("action.invoked", new
        {
            action,
            inputSource,
            windowId,
            windowCount = workspaceCoordinator.Windows.Count,
            tabCount = sessions.Count,
            settingsSelected = settingsTabItem is not null &&
                ReferenceEquals(DocumentTabs.SelectedItem, settingsTabItem),
            activeSessionId = active?.RecoveryId,
            targetSessionId = target?.RecoveryId,
            targetDirty = target?.IsDirty,
            targetReady = target?.IsReady,
            targetSuspended = target?.IsSuspended,
            targetUnloaded = target?.IsUnloaded,
            targetMoving = target?.IsMoving,
            targetExporting = target?.IsExporting,
            targetClosePromptOpen = target?.ClosePromptOpen,
        });
    }

    internal bool IsClosed => resourcesDisposed;

    /// <summary>
    /// Best-effort foreground activation before the exit close flow shows a
    /// dialog. Windows may refuse (foreground lock); callers continue anyway.
    /// </summary>
    internal async Task<bool> ActivateForExitAsync()
    {
        if (resourcesDisposed)
        {
            return false;
        }
        if (isWindowActive)
        {
            return true;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void ActivatedHandler(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                completion.TrySetResult();
            }
        }

        Activated += ActivatedHandler;
        try
        {
            Activate();
            try
            {
                await completion.Task.WaitAsync(ExitActivationTimeout);
            }
            catch (TimeoutException)
            {
                Debug.WriteLine("Window activation timed out during application exit.");
                return false;
            }
            return !resourcesDisposed;
        }
        finally
        {
            Activated -= ActivatedHandler;
        }
    }

    internal async Task<bool> RequestCloseAsync()
    {
        if (resourcesDisposed)
        {
            return true;
        }
        if (windowClosePromptOpen)
        {
            return false;
        }

        windowClosePromptOpen = true;
        try
        {
            if (!await windowClose.ResolveWindowCloseAsync())
            {
                return false;
            }

            allowClose = true;
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void ClosedHandler(object sender, WindowEventArgs args) =>
                completion.TrySetResult();
            Closed += ClosedHandler;
            try
            {
                Close();
                await completion.Task;
            }
            finally
            {
                Closed -= ClosedHandler;
            }
            return true;
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("window.close_failed", exception, new
            {
                windowCount = workspaceCoordinator.Windows.Count,
                tabCount = sessions.Count,
                dirtyTabCount = sessions.Count(session => session.IsDirty),
            });
            Debug.WriteLine(exception);
            return false;
        }
        finally
        {
            windowClosePromptOpen = false;
            if (!resourcesDisposed) ExitCloseBarrier(sessions);
        }
    }

    private Task<bool> YieldToDispatcherAsync()
    {
        var completion = new TaskCompletionSource<bool>();
        if (!DispatcherQueue.TryEnqueue(() => completion.TrySetResult(true)))
        {
            completion.TrySetResult(false);
        }
        return completion.Task;
    }

    private async void OnAppWindowClosing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
        if (allowClose)
        {
            return;
        }

        args.Cancel = true;
        if (windowClosePromptOpen)
        {
            return;
        }

        windowClosePromptOpen = true;
        try
        {
            if (await windowClose.ResolveWindowCloseAsync())
            {
                allowClose = true;
                Close();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
        finally
        {
            windowClosePromptOpen = false;
            if (!resourcesDisposed) ExitCloseBarrier(sessions);
        }
    }

    private DocumentSession? ActiveSession =>
        DocumentTabs.SelectedItem is TabViewItem tab
            ? FindSession(tab)
            : DocumentTabs.SelectedIndex >= 0 &&
                DocumentTabs.SelectedIndex < DocumentTabs.TabItems.Count &&
                DocumentTabs.TabItems[DocumentTabs.SelectedIndex] is TabViewItem indexedTab
                    ? FindSession(indexedTab)
                    : sessions.FirstOrDefault();

}
