using System.Diagnostics;
using System.Runtime.InteropServices;
using ExcalidrawDesktop.Core;
using ExcalidrawDesktop.App.Controls;
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Pages;
using ExcalidrawDesktop.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.StartScreen;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow : Window
{
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
            if (ReferenceEquals(session, ActiveSession)) Title = title;
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
        AppSettingsPage.LoadPreferences(
            desktopPreferences,
            workspaceCoordinator.EffectiveLanguage,
            workspaceCoordinator.StartupLanguagePreference);
        AppSettingsPage.PreferencesChanged += OnPreferencesChanged;
        AppSettingsPage.SettingsPersistenceRetryRequested += OnSettingsPersistenceRetry;
        MainLayout.ActualThemeChanged += OnActualThemeChanged;
        ApplyDesktopTheme();
        ToolTipService.SetToolTip(
            SettingsButton,
            DesktopResources.Get("SettingsButtonToolTip", "Show Settings tab"));
        InitializeTitleBar();
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

    private void OnPreferencesChanged(
        object? sender,
        PreferencesChangedEventArgs args)
    {
        if (!CommandsBlocked) workspaceCoordinator.UpdatePreferences(args.Preferences);
    }

    private void OnSettingsPersistenceRetry(object? sender, EventArgs args) =>
        workspaceCoordinator.UpdatePreferences(desktopPreferences);

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

    internal void ApplySharedPreferences(DesktopPreferences preferences)
    {
        desktopPreferences = preferences;
        var persistenceFailed = workspaceCoordinator.LastSettingsPersistenceResult?.Outcome ==
            ApplicationWorkspaceCoordinator.SettingsPersistenceOutcome.AppliedInMemoryOnly;
        if (!persistenceFailed) AppSettingsPage.ClearSettingsPersistenceFailure();
        AppSettingsPage.LoadPreferences(
            desktopPreferences,
            workspaceCoordinator.EffectiveLanguage,
            workspaceCoordinator.StartupLanguagePreference);
        if (persistenceFailed)
            AppSettingsPage.ShowSettingsPersistenceFailure();
        ApplyDesktopTheme();

        if (!preferences.SuspendInactiveTabs)
        {
            foreach (var session in sessions.Where(session => session.IsSuspended))
            {
                editorSessions.Resume(session);
            }
        }
    }

    private void ApplyDesktopTheme()
    {
        var requestedTheme = desktopPreferences.Theme switch
        {
            DesktopThemePreference.Light => ElementTheme.Light,
            DesktopThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        MainLayout.RequestedTheme = requestedTheme;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyTitleBarTheme();
        SendEditorThemeToAllSessions();
    }

    private void ApplyTitleBarTheme()
    {
        AppWindow.TitleBar.PreferredTheme = MainLayout.ActualTheme == ElementTheme.Dark
            ? TitleBarTheme.Dark
            : TitleBarTheme.Light;
    }

    private void SendEditorThemeToAllSessions()
    {
        foreach (var session in sessions)
        {
            SendEditorTheme(session);
        }
    }

    private void SendEditorTheme(DocumentSession session)
    {
        if (!EditorSessionController.CanPostMessage(session))
        {
            return;
        }

        TryPostEditorMessage(
            session,
            BridgeEventJson.Create(
                "app.themeChanged",
                new { theme = MainLayout.ActualTheme == ElementTheme.Dark ? "dark" : "light" }));
    }

    private void SendEditorLanguage(DocumentSession session)
    {
        if (!EditorSessionController.CanPostMessage(session))
        {
            return;
        }

        var language = workspaceCoordinator.EffectiveLanguage;
        TryPostEditorMessage(
            session,
            BridgeEventJson.Create(
                "app.languageChanged",
                new
                {
                    langCode = language.ExcalidrawCode,
                    direction = language.Direction,
                }));
    }

    private static void TryPostEditorMessage(
        DocumentSession session,
        string message) =>
        session.TryPostEditorMessage(message);

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

    internal void QueueActivatedFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var canonicalPath = DesktopDocumentPath.Normalize(path);
            if (canonicalPath is not null &&
                !pendingActivatedFiles.Contains(
                    canonicalPath,
                    StringComparer.OrdinalIgnoreCase))
            {
                pendingActivatedFiles.Enqueue(canonicalPath);
            }
        }

        if (isWindowReady)
        {
            _ = DrainActivatedFilesAsync();
        }
    }

    internal IReadOnlyList<DocumentSession> OpenSessions => sessions;

    internal bool IsReadyForActivation => isWindowReady && !resourcesDisposed;

    internal void SelectSession(DocumentSession session)
    {
        if (!sessions.Contains(session))
        {
            return;
        }

        DocumentTabs.SelectedItem = session.TabItem;
    }

    private void OnFileDragOver(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(StandardDataFormats.StorageItems))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Copy;
        args.DragUIOverride.Caption = DesktopResources.Get(
            "DragOpenCaption",
            "Open drawing tabs");
        args.DragUIOverride.IsCaptionVisible = true;
        args.Handled = true;
    }

    private async void OnFileDrop(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        args.Handled = true;
        try
        {
            var items = await args.DataView.GetStorageItemsAsync();
            var paths = DesktopDropPaths.SelectSupported(
                items.OfType<StorageFile>().Select(file => file.Path));
            QueueActivatedFiles(paths);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await documents.ShowOpenErrorAsync(DesktopResources.Get(
                "DroppedDrawingsOpenFailed",
                "The dropped drawings could not be opened."));
        }
    }

    private async Task DrainActivatedFilesAsync()
    {
        if (!isWindowReady || openingActivatedFiles)
        {
            return;
        }

        openingActivatedFiles = true;
        try
        {
            while (pendingActivatedFiles.TryDequeue(out var path))
            {
                await documents.OpenPathInTabAsync(path);
            }
        }
        finally
        {
            openingActivatedFiles = false;
        }
    }

    private DocumentSession CreateTab(bool select = true)
    {
        var displayName = NextUntitledName();
        var content = new DocumentTabContent();
        var documentService = new DocumentService(this, content);
        var session = new DocumentSession(
            DesktopTabOrigin.Create(),
            displayName,
            content,
            documentService);
        AttachWindowSessionHost(session);

        session.Content.Visibility = Visibility.Collapsed;
        EditorHost.Children.Add(session.Content);
        sessions.Add(session);
        DocumentTabs.TabItems.Add(session.TabItem);
        UpdateTabHeader(session);

        if (select)
        {
            DocumentTabs.SelectedIndex = DocumentTabs.TabItems.Count - 1;
            foreach (var openSession in sessions)
            {
                openSession.Content.Visibility = ReferenceEquals(
                    openSession,
                    session)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        return session;
    }

    private void AttachWindowSessionHost(DocumentSession session)
    {
        session.DetachWindowHandlers?.Invoke();
        session.DetachWindowHandlers = null;
        session.DocumentService.AttachHost(this, session.Content);
        session.DocumentService.IsPathOwnedByAnotherSession = path =>
            workspaceCoordinator.IsPathOwnedByAnotherSession(session, path);
        session.DocumentService.TryReservePath = path =>
            workspaceCoordinator.TryReservePath(path, session, out var reservation) ? reservation : null;
        session.Dispatcher = new BridgeDispatcher(
            session.DocumentService,
            () => editorSessions.MarkReady(session),
            requestId => windowClose.OnCloseReady(session, requestId),
            isDirty => OnDirtyChanged(session, isDirty),
            () => OnDocumentCreated(session),
            fileName => OnDocumentOpened(session, fileName),
            () => OnDocumentRecovered(session),
            content => documents.OnRecoverySnapshotReceivedAsync(session, content),
            () => _ = documents.CheckExternalFileStateAsync(session, showPrompt: true),
            requestId => windowClose.OnCloseCancelled(session, requestId),
            () => NewTabFromInput("editor"),
            () => documents.RequestOpenDocumentAsync(session),
            () => QueueCloseSession(session),
            next => SelectAdjacentTab(session, next),
            (exportId, message) =>
                _ = imageExports.FailImageExportAsync(session, exportId, message),
            (langCode, direction) =>
                OnEditorLanguageApplied(session, langCode, direction),
            documentLoadFailed: () => editorSessions.HandleLoadFailure(session),
            documentSaved: fileName => OnDocumentSaved(session, fileName))
        {
            IsCloseSavePending = requestId => session.CloseRequestId == requestId,
        };
        EventHandler retryRequested = (_, _) => _ = editorSessions.RetryAsync(session);
        EventHandler closeRequested = (_, _) => QueueCloseSession(session);
        EventHandler webView2HelpRequested = (_, _) =>
            _ = OpenExternalUriAsync(WebView2HelpUri);
        session.Content.RetryRequested += retryRequested;
        session.Content.CloseRequested += closeRequested;
        session.Content.WebView2HelpRequested += webView2HelpRequested;
        session.TabItem.ContextFlyout = CreateTabContextFlyout(session);
        session.DetachWindowHandlers = () =>
        {
            session.Content.RetryRequested -= retryRequested;
            session.Content.CloseRequested -= closeRequested;
            session.Content.WebView2HelpRequested -= webView2HelpRequested;
            session.TabItem.ContextFlyout = null;
            session.DocumentService.IsPathOwnedByAnotherSession = null;
            session.DocumentService.TryReservePath = null;
        };
        ConfigureEditorDropTarget(session, session.Content.Editor);
    }

    private void ConfigureEditorDropTarget(
        DocumentSession session,
        WebView2 editor)
    {
        session.DetachEditorHandlers?.Invoke();
        session.Content.IsEnabled = session.CloseBarrierId is null;
        DragEventHandler dragOver = OnFileDragOver;
        DragEventHandler drop = OnFileDrop;
        editor.AllowDrop = true;
        editor.AddHandler(
            UIElement.DragOverEvent,
            dragOver,
            handledEventsToo: true);
        editor.AddHandler(
            UIElement.DropEvent,
            drop,
            handledEventsToo: true);
        session.DetachEditorHandlers = () =>
        {
            editor.RemoveHandler(UIElement.DragOverEvent, dragOver);
            editor.RemoveHandler(UIElement.DropEvent, drop);
        };
    }

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
        workspaceCoordinator.LibraryChanged -= OnSharedLibraryChanged;
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        DocumentTabs.TabItemsChanged -= OnTabItemsChanged;
        pendingTearOutWindow?.CloseIfEmptyAfterMove();
        pendingTearOutWindow = null;
        AppSettingsPage.PreferencesChanged -= OnPreferencesChanged;
        AppSettingsPage.SettingsPersistenceRetryRequested -= OnSettingsPersistenceRetry;
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

    private async Task RestoreWorkspaceAsync()
    {
        try
        {
            var state = workspaceCoordinator.GetRestoreState(this) ??
                new WorkspaceWindowState(
                    workspaceCoordinator.GetLogicalWindowId(this),
                    null,
                    false,
                    null,
                    Array.Empty<WorkspaceTabState>());

            var initialSession = sessions[0];
            var restoredSessions = new List<DocumentSession>();
            foreach (var savedTab in state.Tabs)
            {
                if (!desktopPreferences.ReopenSavedTabs && !savedTab.WasDirty)
                {
                    continue;
                }

                var path = DesktopDocumentPath.Normalize(savedTab.Path);
                if (path is not null && restoredSessions.Any(session =>
                        DesktopDocumentPath.Equals(
                            session.DocumentService.DocumentPath,
                            path)))
                {
                    continue;
                }

                DocumentSession? target = null;
                try
                {
                    target = restoredSessions.Count == 0
                        ? initialSession
                        : CreateTab(select: false);
                    if (savedTab.WasDirty)
                    {
                        if (savedTab.RecoveryId is not { } recoveryId ||
                            !Guid.TryParseExact(recoveryId, "N", out _))
                        {
                            if (!ReferenceEquals(target, initialSession))
                            {
                                CloseSession(target, discardRecovery: false);
                            }
                            continue;
                        }

                        var content = await recoverySnapshotStore.LoadAsync(recoveryId);
                        if (content is null)
                        {
                            if (!ReferenceEquals(target, initialSession))
                            {
                                CloseSession(target, discardRecovery: false);
                            }
                            continue;
                        }

                        ExcalidrawDocumentValidator.Validate(content);
                        target.RecoveryId = recoveryId;
                        target.RecoveryUpdatedAt = savedTab.RecoveryUpdatedAt;
                        var displayName = savedTab.DisplayName ??
                            (path is null ? DesktopResources.Get("RecoveredDrawingName", "Recovered drawing") : Path.GetFileName(path));
                        await documents.RestoreRecoveryAsync(target, path, displayName, content);
                    }
                    else
                    {
                        if (path is null || !File.Exists(path))
                        {
                            if (!ReferenceEquals(target, initialSession))
                            {
                                CloseSession(target, discardRecovery: false);
                            }
                            continue;
                        }

                        var document = await target.DocumentService.OpenPathAsync(path);
                        target.RecoveryId = savedTab.RecoveryId is { } cleanRecoveryId &&
                            Guid.TryParseExact(cleanRecoveryId, "N", out _)
                                ? cleanRecoveryId
                                : target.RecoveryId;
                        documents.AttachDocumentToSession(target, document, select: false);
                    }
                    restoredSessions.Add(target);
                }
                catch (Exception exception)
                {
                    if (target is not null)
                    {
                        CloseSession(target, discardRecovery: false);
                        if (ReferenceEquals(target, initialSession)) initialSession = sessions[0];
                    }
                    Debug.WriteLine($"Skipped workspace drawing '{path}': {exception}");
                    recentFiles.RemoveAll(recent =>
                        DesktopDocumentPath.Equals(recent, path));
                }
            }

            if (restoredSessions.Count > 0)
            {
                var active = restoredSessions.FirstOrDefault(session =>
                    string.Equals(
                        session.RecoveryId,
                        state.ActiveRecoveryId,
                        StringComparison.OrdinalIgnoreCase)) ??
                    restoredSessions[0];
                DocumentTabs.SelectedItem = active.TabItem;
            }

        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Workspace restore failed: {exception}");
        }
        finally
        {
            restoringWorkspace = false;
            workspaceCoordinator.NotifyRecentFilesChanged();
        }
    }

    private void PopulateRecentFilesMenu()
    {
        if (recentFiles.RemoveAll(path => !File.Exists(path)) > 0)
        {
            QueuePersistWorkspace();
            QueueJumpListUpdate();
        }

        RecentFilesMenu.Items.Clear();
        if (recentFiles.Count == 0)
        {
            RecentFilesMenu.Items.Add(new MenuFlyoutItem
            {
                Text = DesktopResources.Get(
                    "RecentFilesEmpty",
                    "No recent drawings"),
                IsEnabled = false,
            });
            return;
        }

        foreach (var path in recentFiles)
        {
            var item = new MenuFlyoutItem { Text = Path.GetFileName(path) };
            ToolTipService.SetToolTip(item, path);
            AutomationProperties.SetHelpText(item, path);
            item.Click += (_, _) => _ = documents.OpenRecentFileAsync(path);
            RecentFilesMenu.Items.Add(item);
        }

        RecentFilesMenu.Items.Add(new MenuFlyoutSeparator());
        var clear = new MenuFlyoutItem
        {
            Text = DesktopResources.Get(
                "ClearRecentDrawings",
                "Clear recent drawings"),
        };
        clear.Click += (_, _) =>
        {
            recentFiles.Clear();
            workspaceCoordinator.NotifyRecentFilesChanged();
        };
        RecentFilesMenu.Items.Add(clear);
    }

    internal void RefreshRecentFiles()
    {
        PopulateRecentFilesMenu();
        QueueJumpListUpdate();
    }

    private void AddRecentFile(string path)
    {
        var canonicalPath = DesktopDocumentPath.Normalize(path);
        if (canonicalPath is null)
        {
            return;
        }

        recentFiles.RemoveAll(recent =>
            !File.Exists(recent) ||
            DesktopDocumentPath.Equals(recent, canonicalPath));
        recentFiles.Insert(0, canonicalPath);
        if (recentFiles.Count > 10)
        {
            recentFiles.RemoveRange(10, recentFiles.Count - 10);
        }
        workspaceCoordinator.NotifyRecentFilesChanged();
    }

    private void QueueJumpListUpdate()
    {
        jumpListUpdateQueued = true;
        if (jumpListUpdateRunning)
        {
            return;
        }

        jumpListUpdateRunning = true;
        _ = UpdateJumpListAsync();
    }

    private async Task UpdateJumpListAsync()
    {
        try
        {
            if (!JumpList.IsSupported())
            {
                jumpListUpdateQueued = false;
                return;
            }

            while (jumpListUpdateQueued)
            {
                jumpListUpdateQueued = false;
                var paths = recentFiles
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToArray();
                var jumpList = await JumpList.LoadCurrentAsync();
                jumpList.SystemGroupKind = JumpListSystemGroupKind.None;
                jumpList.Items.Clear();
                foreach (var path in paths)
                {
                    var item = JumpListItem.CreateWithArguments(
                        DesktopLaunchFile.FormatArguments(path),
                        Path.GetFileName(path));
                    item.Description = path;
                    item.GroupName = "Recent drawings";
                    jumpList.Items.Add(item);
                }

                await jumpList.SaveAsync();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Jump list update failed: {exception}");
        }
        finally
        {
            jumpListUpdateRunning = false;
            if (jumpListUpdateQueued)
            {
                QueueJumpListUpdate();
            }
        }
    }

    private void OnDirtyChanged(DocumentSession session, bool isDirty)
    {
        if (!sessions.Contains(session))
        {
            return;
        }

        if (!isDirty && session.IsRetrying &&
            session.PendingDocumentLoad?.IsRecovery == true)
        {
            return;
        }

#if DEBUG
        if (session.ForceDirtyForSmoke && !isDirty)
        {
            return;
        }
#endif
        session.IsDirty = isDirty;
        session.DocumentStateVersion++;
        if (!ReferenceEquals(session, ActiveSession))
        {
            session.InactiveSince = DateTimeOffset.UtcNow;
        }
        if (!isDirty)
        {
            session.RecoveryUpdatedAt = null;
            session.RecoveryFailed = false;
            _ = documents.DeleteRecoverySnapshotAsync(session, onlyIfClean: true);
        }
        UpdateTabHeader(session);
        UpdateWindowTitle();
        QueuePersistWorkspace();
    }

    private void OnDocumentCreated(DocumentSession session)
    {
        _ = documents.DeleteRecoverySnapshotAsync(session);
        session.RecoveryId = Guid.NewGuid().ToString("N");
        session.RecoveryUpdatedAt = null;
        session.ExternalFileState = ExternalFileState.None;
        session.DisplayName = NextUntitledName();
        UpdateTabHeader(session);
        UpdateWindowTitle();
        QueuePersistWorkspace();
    }

    private void OnDocumentOpened(DocumentSession session, string fileName)
    {
        session.PendingDocumentLoad = null;
        editorSessions.CompleteRetry(session);
        if (session.IsRestoringFromHibernation)
        {
            session.DisplayName = fileName;
            editorSessions.CompleteHibernationRestore(session);
            UpdateWindowTitle();
            if (session.DocumentService.DocumentPath is { } restoredPath)
            {
                AddRecentFile(restoredPath);
                _ = documents.CheckExternalFileStateAsync(session, showPrompt: false);
            }
            return;
        }

        session.RecoveryUpdatedAt = null;
        _ = documents.DeleteRecoverySnapshotAsync(session, onlyIfClean: true);
        session.ExternalFileState = ExternalFileState.None;
        session.DisplayName = fileName;
        UpdateTabHeader(session);
        UpdateWindowTitle();
        if (session.DocumentService.DocumentPath is { } path)
        {
            documents.WatchExternalFile(session, path);
            AddRecentFile(path);
            _ = documents.CheckExternalFileStateAsync(session, showPrompt: false);
        }
    }

    private void OnDocumentLoadApplied(DocumentSession session, Guid loadId, string fileName, bool isRecovery)
    {
        if (session.PendingDocumentLoad is not { } pending || pending.LoadId != loadId ||
            pending.FileName != fileName || pending.IsRecovery != isRecovery) return;
        if (!isRecovery && !session.DocumentService.ConfirmOpened(fileName, loadId)) return;
        session.PendingPathReservation?.Dispose();
        session.PendingPathReservation = null;
        if (isRecovery)
        {
            OnDocumentRecovered(session);
            workspaceCoordinator.ReleasePreservedRecovery(session.RecoveryId);
        }
        else
        {
            OnDocumentOpened(session, fileName);
        }
    }

    private void OnDocumentSaved(DocumentSession session, string fileName)
    {
        if (!sessions.Contains(session)) return;
        // The saved revision can precede the current editor revision. Only a
        // subsequent clean acknowledgement may delete recovery for this scene.
        session.DisplayName = fileName;
        session.ExternalFileState = ExternalFileState.None;
        _ = documents.RefreshRecoveryBaselineAsync(session);
        if (session.DocumentService.DocumentPath is { } path)
        {
            documents.WatchExternalFile(session, path);
            AddRecentFile(path);
        }
        UpdateTabHeader(session);
        UpdateWindowTitle();
    }

    private void OnDocumentRecovered(DocumentSession session)
    {
        if (!sessions.Contains(session))
        {
            return;
        }

        session.PendingDocumentLoad = null;
        session.IsDirty = true;
        editorSessions.CompleteRetry(session);
        UpdateTabHeader(session);
        UpdateWindowTitle();
#if DEBUG
        if (smoke.VerifyRecoverySmoke)
        {
            recoveryTabsRestored++;
            if (sessions.FirstOrDefault(openSession =>
                    openSession.PendingDocumentLoad is not null) is { } pending)
            {
                DocumentTabs.SelectedItem = pending.TabItem;
            }
            else if (recoveryTabsRestored == sessions.Count)
            {
                Title = "Excalidraw Desktop — Recovery smoke restored";
            }
        }
#endif
        QueuePersistWorkspace();
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

    private void OnAddTabButtonClick(TabView sender, object args)
    {
        if (CommandsBlocked) return;
        LogAction("tab.new", "tab_button");
        CreateTab();
    }

    private void OnFileNewTabClick(object sender, RoutedEventArgs args) =>
        NewTabFromInput("menu");

    private void OnFileNewWindowClick(object sender, RoutedEventArgs args) =>
        NewWindowFromInput("menu");

    private void OnFileOpenClick(object sender, RoutedEventArgs args) =>
        OpenFromInput("menu");

    private void OnFileSaveClick(object sender, RoutedEventArgs args) =>
        SaveFromInput(saveAs: false, "menu");

    private void OnFileSaveAsClick(object sender, RoutedEventArgs args) =>
        SaveFromInput(saveAs: true, "menu");

    // Menu items and keyboard accelerators share these so behaviour cannot
    // drift between the two input paths; only the logged source differs.
    private void NewTabFromInput(string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction("tab.new", inputSource);
        CreateTab();
    }

    private void NewWindowFromInput(string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction("window.new", inputSource);
        workspaceCoordinator.CreateWindow();
    }

    private void OpenFromInput(string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction("document.open", inputSource);
        var session = ActiveSession ?? lastDocumentSession ?? sessions.FirstOrDefault() ?? CreateTab();
        _ = documents.RequestOpenDocumentAsync(session);
    }

    private void SaveFromInput(bool saveAs, string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction(saveAs ? "document.save_as" : "document.save", inputSource);
        RequestSaveFromFileMenu(saveAs);
    }

    private void SaveAllFromInput(string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction("document.save_all", inputSource);
        _ = SaveAllFromFileMenuAsync();
    }

    private void CloseActiveTabFromInput(string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction("tab.close", inputSource);
        if (settingsTabItem is not null &&
            ReferenceEquals(DocumentTabs.SelectedItem, settingsTabItem))
        {
            QueueHideSettingsPage();
            return;
        }

        if (ActiveSession is { } session)
        {
            QueueCloseSession(session);
        }
    }

    private void CloseWindowFromInput(string inputSource)
    {
        LogAction("window.close", inputSource);
        Close();
    }

    private async void OnFileExportPngClick(object sender, RoutedEventArgs args)
    {
        if (CommandsBlocked) return;
        LogAction("document.export_png", "menu");
        await imageExports.ExportActiveSessionAsPngAsync();
    }

    private void RequestSaveFromFileMenu(bool saveAs)
    {
        if (CommandsBlocked || ActiveSession is not { } session ||
            !EditorSessionController.CanRequestSave(session))
        {
            return;
        }

        session.TryPostEditorMessage(
            BridgeEventJson.Create(
                "document.saveRequested",
                new { reason = saveAs ? "saveAs" : "save" }));
    }

    private void OnFileSaveAllClick(object sender, RoutedEventArgs args) =>
        SaveAllFromInput("menu");

    private async Task SaveAllFromFileMenuAsync()
    {
        if (CommandsBlocked) return;
        var dirtySessions = sessions.Where(session => session.IsDirty).ToArray();
        if (dirtySessions.Length == 0)
        {
            return;
        }

        var originallyActive = ActiveSession;
        await windowClose.SaveAllForWindowCloseAsync(dirtySessions);
        if (originallyActive is not null && sessions.Contains(originallyActive))
        {
            DocumentTabs.SelectedItem = originallyActive.TabItem;
        }
    }

    private void OnFileCloseTabClick(object sender, RoutedEventArgs args) =>
        CloseActiveTabFromInput("menu");

    private void OnFileCloseWindowClick(object sender, RoutedEventArgs args) =>
        CloseWindowFromInput("menu");

    private void OnFileExitClick(object sender, RoutedEventArgs args)
    {
        LogAction("application.exit", "menu");
        _ = workspaceCoordinator.RequestExitAsync();
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

    private void OnSettingsClick(object sender, RoutedEventArgs args)
    {
        LogAction("settings.open", "menu");
        ShowSettingsPage();
    }

    private void OnSettingsButtonClick(object sender, RoutedEventArgs args)
    {
        LogAction("settings.open", "button");
        ShowSettingsPage();
    }

    private void ShowSettingsPage()
    {
        if (CommandsBlocked) return;
        AppSettingsPage.LoadPreferences(
            desktopPreferences,
            workspaceCoordinator.EffectiveLanguage,
            workspaceCoordinator.StartupLanguagePreference);
        if (settingsTabItem is null)
        {
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            header.Children.Add(new FontIcon
            {
                Glyph = "\uE713",
                FontSize = 14,
            });
            header.Children.Add(new TextBlock
            {
                Text = DesktopResources.Get("SettingsTabTitle", "Settings"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            settingsTabItem = new TabViewItem
            {
                Header = header,
                IsClosable = true,
                CanDrag = false,
            };
            AutomationProperties.SetName(
                settingsTabItem,
                DesktopResources.Get("SettingsTabTitle", "Settings"));
            AutomationProperties.SetAutomationId(settingsTabItem, "SettingsTab");
            DocumentTabs.TabItems.Add(settingsTabItem);
        }

        DocumentTabs.SelectedItem = settingsTabItem;
    }

    private void HideSettingsPage()
    {
        if (settingsTabItem is null)
        {
            return;
        }

        var tab = settingsTabItem;
        settingsTabItem = null;
        settingsPageVisible = false;
        AppSettingsPage.Visibility = Visibility.Collapsed;
        EditorHost.Visibility = Visibility.Visible;
        DocumentTabs.TabItems.Remove(tab);
        var target = lastDocumentSession is not null &&
            sessions.Contains(lastDocumentSession)
                ? lastDocumentSession
                : sessions.FirstOrDefault();
        if (target is not null)
        {
            DocumentTabs.SelectedItem = target.TabItem;
        }
        UpdateWindowTitle();
    }

    private void QueueHideSettingsPage() =>
        DispatcherQueue.TryEnqueue(HideSettingsPage);

    private void OnNewTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NewTabFromInput("keyboard");
    }

    private void OnNewWindowAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NewWindowFromInput("keyboard");
    }

    private void OnCloseTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CloseActiveTabFromInput("keyboard");
    }

    private void OnOpenAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenFromInput("keyboard");
    }

    private void OnSaveAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SaveFromInput(saveAs: false, "keyboard");
    }

    private void OnSaveAsAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SaveFromInput(saveAs: true, "keyboard");
    }

    private void OnCloseWindowAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CloseWindowFromInput("keyboard");
    }

    private void OnSaveAllAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SaveAllFromInput("keyboard");
    }

    private void OnNextTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        LogAction("tab.next", "keyboard");
        if (DocumentTabs.SelectedItem is TabViewItem tab)
        {
            SelectAdjacentTabItem(tab, next: true);
        }
    }

    private void OnPreviousTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        LogAction("tab.previous", "keyboard");
        if (DocumentTabs.SelectedItem is TabViewItem tab)
        {
            SelectAdjacentTabItem(tab, next: false);
        }
    }

    private void OnTabCloseRequested(
        TabView sender,
        TabViewTabCloseRequestedEventArgs args)
    {
        LogAction("tab.close", "tab", FindSession(args.Tab));
        if (settingsTabItem is not null && ReferenceEquals(args.Tab, settingsTabItem))
        {
            QueueHideSettingsPage();
            return;
        }

        if (FindSession(args.Tab) is { } session)
        {
            QueueCloseSession(session);
        }
    }

    private void QueueCloseSession(DocumentSession session)
    {
        if (CommandsBlocked) return;
        _ = RequestCloseSessionAndReportAsync(session);
    }

    private async Task<bool> RequestCloseSessionAndReportAsync(
        DocumentSession session)
    {
        try
        {
            return await windowClose.RequestCloseSessionAsync(session);
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("tab.close_failed", exception, new
            {
                sessionId = session.RecoveryId,
                session.IsDirty,
                session.IsReady,
                session.IsSuspended,
                session.IsUnloaded,
            });
            Debug.WriteLine($"Tab close failed: {exception}");
            return false;
        }
    }

    private void OnTabTearOutWindowRequested(
        TabView sender,
        TabViewTabTearOutWindowRequestedEventArgs args)
    {
        var tab = args.Tabs.OfType<TabViewItem>().FirstOrDefault();
        var session = tab is null ? null : FindSession(tab);
        LogAction("tab.tear_out_started", "drag", session);
        if (session is null || !CanMoveSession(session))
        {
            return;
        }

        pendingTearOutWindow?.CloseIfEmptyAfterMove();
        pendingTearOutWindow = workspaceCoordinator.CreateTearOutPlaceholder();
        args.NewWindowId = pendingTearOutWindow.AppWindow.Id;
    }

    private void OnTabItemsChanged(
        TabView sender,
        Windows.Foundation.Collections.IVectorChangedEventArgs args)
    {
        var orderedSessions = GetOrderedSessions();
        if (orderedSessions.Count == sessions.Count &&
            !orderedSessions.SequenceEqual(sessions))
        {
            sessions.Clear();
            sessions.AddRange(orderedSessions);
            QueuePersistWorkspace();
        }
    }

    private void OnTabDragCompleted(
        TabView sender,
        TabViewTabDragCompletedEventArgs args)
    {
        // TabTearOutRequested consumes and clears a successful destination.
        // If the drag ended without that event, discard the hidden placeholder
        // so it cannot linger in the workspace or receive future activation.
        pendingTearOutWindow?.CloseIfEmptyAfterMove();
        pendingTearOutWindow = null;
    }

    private void OnTabTearOutRequested(
        TabView sender,
        TabViewTabTearOutRequestedEventArgs args)
    {
        var destination = pendingTearOutWindow;
        pendingTearOutWindow = null;
        var tab = args.Tabs.OfType<TabViewItem>().FirstOrDefault();
        var session = tab is null ? null : FindSession(tab);
        LogAction("tab.tear_out_completed", "drag", session);
        if (destination is null || session is null ||
            !workspaceCoordinator.MoveSession(this, session, destination))
        {
            destination?.CloseIfEmptyAfterMove();
        }
    }

    private void OnExternalTornOutTabsDropping(
        TabView sender,
        TabViewExternalTornOutTabsDroppingEventArgs args)
    {
        var tab = args.Tabs.OfType<TabViewItem>().FirstOrDefault();
        args.AllowDrop = tab is not null &&
            workspaceCoordinator.FindSessionByTab(tab) is
            {
                Window: var source,
                Session: var session,
            } &&
            !ReferenceEquals(source, this) &&
            source.CanMoveSession(session);
    }

    private void OnExternalTornOutTabsDropped(
        TabView sender,
        TabViewExternalTornOutTabsDroppedEventArgs args)
    {
        var tab = args.Tabs.OfType<TabViewItem>().FirstOrDefault();
        if (tab is null ||
            workspaceCoordinator.FindSessionByTab(tab) is not
            {
                Window: var source,
                Session: var session,
            } ||
            ReferenceEquals(source, this))
        {
            return;
        }

        LogAction("tab.external_drop", "drag", session);
        workspaceCoordinator.MoveSession(source, session, this, args.DropIndex);
    }

    private void OnTabSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        LogAction("tab.selected", "tab");
        var settingsSelected = settingsTabItem is not null &&
            ReferenceEquals(DocumentTabs.SelectedItem, settingsTabItem);
        settingsPageVisible = settingsSelected;
        AppSettingsPage.Visibility = settingsSelected
            ? Visibility.Visible
            : Visibility.Collapsed;
        EditorHost.Visibility = settingsSelected
            ? Visibility.Collapsed
            : Visibility.Visible;
        var active = ActiveSession;
        var now = DateTimeOffset.UtcNow;
        foreach (var session in sessions)
        {
            var isActive = ReferenceEquals(session, active);
            if (isActive)
            {
                if (session.IsUnloaded)
                {
                    _ = editorSessions.WakeAsync(session);
                }
                else
                {
                    editorSessions.Resume(session);
                }
                session.InactiveSince = null;
            }
            else
            {
                session.InactiveSince ??= now;
            }
            session.Content.Visibility = isActive
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        UpdateWindowTitle();
        if (settingsSelected)
        {
            AppSettingsPage.Focus(FocusState.Programmatic);
            QueuePersistWorkspace();
            return;
        }

        if (isWindowReady && active is { } activeSession)
        {
            lastDocumentSession = activeSession;
            if (!activeSession.IsUnloaded &&
                !activeSession.IsRestoringFromHibernation)
            {
                activeSession.Content.Editor.Focus(FocusState.Programmatic);
                _ = editorSessions.InitializeAsync(activeSession);
            }
            _ = documents.CheckExternalFileStateAsync(activeSession, showPrompt: true);
        }
        QueuePersistWorkspace();
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

    internal DocumentSessionTransfer? DetachSessionForMove(
        DocumentSession session)
    {
        if (!CanMoveSession(session))
        {
            return null;
        }

        var sourceIndex = DocumentTabs.TabItems.IndexOf(session.TabItem);
        if (sourceIndex < 0)
        {
            return null;
        }

        session.IsMoving = true;
        var coreWebView = session.CoreWebView;
        session.DetachExternalFileWatcher();
        session.DetachWindowHandlers?.Invoke();
        session.DetachWindowHandlers = null;
        session.DetachEditorHandlers?.Invoke();
        session.DetachEditorHandlers = null;
        EditorSessionController.DetachWebView(session);
        sessions.Remove(session);
        DocumentTabs.TabItems.Remove(session.TabItem);
        EditorHost.Children.Remove(session.Content);
        if (ReferenceEquals(lastDocumentSession, session))
        {
            lastDocumentSession = sessions.FirstOrDefault();
        }
        UpdateWindowTitle();
        return new DocumentSessionTransfer(session, coreWebView, sourceIndex);
    }

    internal void AttachMovedSession(
        DocumentSessionTransfer transfer,
        int? index = null)
    {
        var session = transfer.Session;
        if (sessions.Contains(session))
        {
            throw new InvalidOperationException(
                "The drawing session is already attached to this window.");
        }

        var insertIndex = Math.Clamp(
            index ?? DocumentTabs.TabItems.Count,
            0,
            DocumentTabs.TabItems.Count);
        sessions.Insert(Math.Min(insertIndex, sessions.Count), session);
        EditorHost.Children.Add(session.Content);
        DocumentTabs.TabItems.Insert(insertIndex, session.TabItem);
        AttachWindowSessionHost(session);
        if (transfer.CoreWebView is { } coreWebView)
        {
            editorSessions.AttachWebView(session, coreWebView);
        }
        if (session.DocumentService.DocumentPath is { } path)
        {
            documents.WatchExternalFile(session, path);
        }

        session.IsMoving = false;
        session.Content.Visibility = Visibility.Visible;
        DocumentTabs.SelectedItem = session.TabItem;
        foreach (var openSession in sessions)
        {
            openSession.Content.Visibility = ReferenceEquals(openSession, session)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        UpdateTabHeader(session);
        UpdateWindowTitle();
        QueuePersistWorkspace();
    }

    internal void CloseIfEmptyAfterMove()
    {
        if (sessions.Count != 0)
        {
            return;
        }

        allowClose = true;
        Close();
    }

    private bool CanMoveSession(DocumentSession session) =>
        !CommandsBlocked && !WindowModalCoordinator.For(this).IsBusy &&
        sessions.Contains(session) &&
        session.IsReady &&
        !session.IsRetrying &&
        !session.IsSuspended &&
        !session.IsUnloaded &&
        !session.IsRestoringFromHibernation &&
        session.PendingDocumentLoad is null &&
        SessionMoveRules.CanMove(new SessionMoveState(
            session.IsMoving,
            session.IsInitializing,
            session.IsBridgeDispatching,
            session.CloseCompletion is not null ||
                session.WindowCloseSaveCompletion is not null,
            session.ClosePromptOpen ||
                session.ExternalConflictPromptOpen ||
                windowClosePromptOpen ||
                documents.IsPickerActive,
            session.IsSuspensionChanging,
            session.IsResuming,
            session.IsUnloading,
            session.IsExporting));

    private void CloseSession(DocumentSession session, bool discardRecovery = true)
    {
        if (!sessions.Remove(session))
        {
            return;
        }

        DocumentTabs.TabItems.Remove(session.TabItem);
        EditorHost.Children.Remove(session.Content);
        if (discardRecovery) _ = documents.DeleteRecoverySnapshotAsync(session);
        session.Dispose();
        if (ReferenceEquals(lastDocumentSession, session))
        {
            lastDocumentSession = sessions.FirstOrDefault();
        }

        if (sessions.Count == 0 && !allowClose)
        {
            CreateTab();
        }

        UpdateWindowTitle();
        QueuePersistWorkspace();
    }

    private void SelectAdjacentTab(DocumentSession source, bool next)
    {
        SelectAdjacentTabItem(source.TabItem, next);
    }

    private void SelectAdjacentTabItem(TabViewItem source, bool next)
    {
        var tabs = DocumentTabs.TabItems.OfType<TabViewItem>().ToArray();
        if (tabs.Length < 2)
        {
            return;
        }

        var sourceIndex = Array.IndexOf(tabs, source);
        if (sourceIndex < 0)
        {
            return;
        }

        var destinationIndex = next
            ? (sourceIndex + 1) % tabs.Length
            : (sourceIndex - 1 + tabs.Length) % tabs.Length;
        DocumentTabs.SelectedItem = tabs[destinationIndex];
    }

    private MenuFlyout CreateTabContextFlyout(DocumentSession session)
    {
        var flyout = new MenuFlyout();
        var newTab = new MenuFlyoutItem
        {
            Text = DesktopResources.Get("ContextNewTab", "New Tab"),
        };
        newTab.Click += (_, _) => NewTabFromInput("context_menu");

        var copyPath = new MenuFlyoutItem
        {
            Text = DesktopResources.Get("ContextCopyPath", "Copy Path"),
        };
        AutomationProperties.SetAutomationId(copyPath, "CopyTabPath");
        copyPath.Click += (_, _) => _ = CopyDocumentPathAsync(session);

        var reveal = new MenuFlyoutItem
        {
            Text = DesktopResources.Get(
                "ContextRevealInExplorer",
                "Reveal in Explorer"),
        };
        AutomationProperties.SetAutomationId(reveal, "RevealTabInExplorer");
        reveal.Click += (_, _) => _ = RevealDocumentInExplorerAsync(session);

        var moveToNewWindow = new MenuFlyoutItem
        {
            Text = DesktopResources.Get(
                "ContextMoveToNewWindow",
                "Move Tab to New Window"),
        };
        AutomationProperties.SetAutomationId(
            moveToNewWindow,
            "MoveTabToNewWindow");
        moveToNewWindow.Click += (_, _) =>
            workspaceCoordinator.MoveSessionToNewWindow(this, session);

        var close = new MenuFlyoutItem
        {
            Text = DesktopResources.Get("ContextCloseTab", "Close Tab"),
        };
        close.Click += (_, _) => QueueCloseSession(session);

        var closeOthers = new MenuFlyoutItem
        {
            Text = DesktopResources.Get(
                "ContextCloseOtherTabs",
                "Close Other Tabs"),
        };
        closeOthers.Click += (_, _) => _ = CloseSessionsAsync(
            WorkspaceTabOperations.OtherTabs(GetOrderedSessions(), session));

        var closeRight = new MenuFlyoutItem
        {
            Text = DesktopResources.Get(
                "ContextCloseTabsRight",
                "Close Tabs to the Right"),
        };
        closeRight.Click += (_, _) => _ = CloseSessionsAsync(
            WorkspaceTabOperations.TabsToRight(GetOrderedSessions(), session));

        flyout.Opening += (_, _) =>
        {
            var path = session.DocumentService.DocumentPath;
            copyPath.IsEnabled = path is not null;
            reveal.IsEnabled = path is not null && File.Exists(path);
            moveToNewWindow.IsEnabled = CanMoveSession(session);
        };

        flyout.Items.Add(newTab);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(copyPath);
        flyout.Items.Add(reveal);
        flyout.Items.Add(moveToNewWindow);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(close);
        flyout.Items.Add(closeOthers);
        flyout.Items.Add(closeRight);
        return flyout;
    }

    private async Task CopyDocumentPathAsync(DocumentSession session)
    {
        if (session.DocumentService.DocumentPath is not { } path)
        {
            return;
        }

        try
        {
            var data = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy,
            };
            data.SetText(path);
            Clipboard.SetContent(data);
            Clipboard.Flush();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await ShowFileLocationErrorAsync(
                DesktopResources.Get(
                    "DrawingPathCopyFailed",
                    "The drawing path could not be copied."));
        }
    }

    private async Task RevealDocumentInExplorerAsync(DocumentSession session)
    {
        if (session.DocumentService.DocumentPath is not { } path)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!File.Exists(path) || string.IsNullOrWhiteSpace(directory))
            {
                await ShowFileLocationErrorAsync(
                    DesktopResources.Get(
                        "DrawingLocationMissing",
                        "The drawing is no longer at its saved location."));
                return;
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            var options = new FolderLauncherOptions();
            options.ItemsToSelect.Add(file);
            if (!await Launcher.LaunchFolderAsync(folder, options))
            {
                await ShowFileLocationErrorAsync(
                    DesktopResources.Get(
                        "ExplorerShowDrawingFailed",
                        "File Explorer could not show this drawing."));
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await ShowFileLocationErrorAsync(
                DesktopResources.Get(
                    "DrawingLocationMissing",
                    "The drawing is no longer at its saved location."));
        }
    }

    private async Task ShowFileLocationErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = DocumentTabs.XamlRoot,
            Title = DesktopResources.Get(
                "FileLocationUnavailableTitle",
                "File location unavailable"),
            Content = message,
            CloseButtonText = DesktopResources.Get("OkButton", "OK"),
            DefaultButton = ContentDialogButton.Close,
        };
        await WindowModalCoordinator.For(this).RunAsync(async () => await dialog.ShowAsync());
    }

    private async Task CloseSessionsAsync(IEnumerable<DocumentSession> targets)
    {
        if (CommandsBlocked) return;
        foreach (var session in targets.ToArray())
        {
            if (sessions.Contains(session) &&
                !await windowClose.RequestCloseSessionAsync(session))
            {
                return;
            }
        }
    }

    private IReadOnlyList<DocumentSession> GetOrderedSessions() =>
        DocumentTabs.TabItems
            .OfType<TabViewItem>()
            .Select(FindSession)
            .OfType<DocumentSession>()
            .ToArray();

    private void SynchronizeSessionOrder()
    {
        var orderedSessions = GetOrderedSessions();
        if (orderedSessions.Count != sessions.Count)
        {
            return;
        }

        sessions.Clear();
        sessions.AddRange(orderedSessions);
        QueuePersistWorkspace();
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

    private void QueuePersistWorkspace()
    {
        if (restoringWorkspace)
        {
            return;
        }

        workspaceCoordinator.QueuePersistWorkspace();
    }

    private async Task PersistWorkspaceAsync(bool treatDirtyAsClean = false)
    {
        if (restoringWorkspace)
        {
            return;
        }

        try
        {
            await workspaceCoordinator.PersistWorkspaceAsync(
                treatDirtyAsClean ? this : null);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Workspace persistence failed: {exception}");
        }
    }

    internal WorkspaceWindowState CaptureWorkspaceState(
        bool treatDirtyAsClean = false,
        bool capturePlacement = true)
    {
        var activeSession = ActiveSession ??
            (lastDocumentSession is not null && sessions.Contains(lastDocumentSession)
                ? lastDocumentSession
                : null);
        WorkspaceWindowBounds? bounds = null;
        var isMaximized = false;
        try
        {
            if (capturePlacement)
            {
                var position = AppWindow.Position;
                var size = AppWindow.Size;
                bounds = new WorkspaceWindowBounds(
                    position.X,
                    position.Y,
                    size.Width,
                    size.Height);
                isMaximized = AppWindow.Presenter is OverlappedPresenter presenter &&
                    presenter.State == OverlappedPresenterState.Maximized;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Window placement capture skipped during close: {exception}");
        }
        return new WorkspaceWindowState(
            workspaceCoordinator.GetLogicalWindowId(this),
            bounds,
            isMaximized,
            activeSession?.RecoveryId,
            GetOrderedSessions()
                .Where(session =>
                    session.DocumentService.DocumentPath is not null ||
                    (!treatDirtyAsClean && session.IsDirty))
                .Select(session => new WorkspaceTabState(
                    session.DocumentService.DocumentPath,
                    !treatDirtyAsClean && session.IsDirty,
                    session.RecoveryId,
                    session.DisplayName,
                    session.RecoveryUpdatedAt))
                .ToArray());
    }

    private void RestoreWindowPlacement()
    {
        var state = workspaceCoordinator.GetRestoreState(this);
        if (state?.Bounds is { Width: >= 320, Height: >= 240 } bounds)
        {
            var requested = new RectInt32(
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height);
            var display = DisplayArea.GetFromRect(
                requested,
                DisplayAreaFallback.Primary);
            var workArea = display.WorkArea;
            var width = Math.Min(requested.Width, workArea.Width);
            var height = Math.Min(requested.Height, workArea.Height);
            var x = Math.Clamp(
                requested.X,
                workArea.X,
                workArea.X + workArea.Width - width);
            var y = Math.Clamp(
                requested.Y,
                workArea.Y,
                workArea.Y + workArea.Height - height);
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }

        if (state?.IsMaximized == true &&
            AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private DocumentSession? FindSession(TabViewItem tab) =>
        sessions.FirstOrDefault(session => ReferenceEquals(session.TabItem, tab));

    private string NextUntitledName()
    {
        untitledSequence++;
        var untitled = DesktopResources.Get("UntitledName", "Untitled");
        return untitledSequence == 1
            ? untitled
            : DesktopResources.Format(
                "UntitledNumberedFormat",
                "{0} {1}",
                untitled,
                untitledSequence);
    }

    private void UpdateTabHeader(DocumentSession session)
    {
        session.TabItem.Header = session.HeaderText;
        ToolTipService.SetToolTip(session.TabItem, session.DisplayName);
        var accessibilityStates = new List<string>();
        if (session.DocumentService.DocumentPath is null)
        {
            accessibilityStates.Add(DesktopResources.Get(
                "TabStateNewDrawing",
                "new drawing"));
        }
        accessibilityStates.Add(session.IsDirty
            ? DesktopResources.Get("TabStateUnsaved", "unsaved changes")
            : DesktopResources.Get("TabStateSaved", "saved"));
        if (session.IsSuspended || session.IsUnloaded)
        {
            accessibilityStates.Add(DesktopResources.Get(
                "TabStateSleeping",
                "sleeping"));
        }
        if (session.ExternalFileState is not ExternalFileState.None)
        {
            accessibilityStates.Add(DesktopResources.Get(
                "TabStateChangedOnDisk",
                "changed on disk"));
        }

        AutomationProperties.SetName(
            session.TabItem,
            DesktopResources.Format(
                "TabAutomationNameFormat",
                "{0}, {1}",
                session.DisplayName,
                string.Join(", ", accessibilityStates)));
        AutomationProperties.SetHelpText(
            session.TabItem,
            session.DocumentService.DocumentPath ?? DesktopResources.Get(
                "UnsavedDrawingHelpText",
                "Unsaved drawing"));
    }

    private void UpdateWindowTitle()
    {
        var active = ActiveSession;
        UpdateFileMenuState(active);
        UpdateStatusBar(active);
        if (settingsPageVisible)
        {
            Title = DesktopResources.Get(
                "SettingsWindowTitle",
                "Settings — Excalidraw Desktop");
            return;
        }
        if (active is null || !active.IsReady)
        {
            Title = DesktopResources.Get(
                "StartingWindowTitle",
                "Excalidraw Desktop — Starting…");
            return;
        }

        var dirtyMarker = active.IsDirty ? " ●" : string.Empty;
        Title = active.DocumentService.DocumentPath is null
            ? DesktopResources.Format(
                "ApplicationWindowTitleFormat",
                "Excalidraw Desktop{0}",
                dirtyMarker)
            : DesktopResources.Format(
                "DrawingWindowTitleFormat",
                "{0}{1} — Excalidraw Desktop",
                active.DisplayName,
                dirtyMarker);
        if (active.ExternalFileState is not ExternalFileState.None)
        {
            Title += DesktopResources.Get(
                "ChangedOnDiskTitleSuffix",
                " — Changed on disk");
        }
    }

    private void UpdateFileMenuState(DocumentSession? active)
    {
        var canUseEditor = !settingsPageVisible && active is
        {
            IsReady: true,
            IsRetrying: false,
            PendingDocumentLoad: null,
            IsResuming: false,
            IsRestoringFromHibernation: false,
            CoreWebView: not null,
        };
        SaveMenuItem.IsEnabled = canUseEditor;
        SaveAsMenuItem.IsEnabled = canUseEditor;
        ExportPngMenuItem.IsEnabled = canUseEditor && active?.IsExporting != true;
        SaveAllMenuItem.IsEnabled = !settingsPageVisible && sessions.Any(session =>
            session.IsDirty &&
            EditorSessionController.CanRequestSave(session));
        CloseTabMenuItem.IsEnabled = settingsPageVisible || active is not null;
    }

    private void UpdateStatusBar(DocumentSession? session)
    {
        if (settingsPageVisible)
        {
            StatusBar.Visibility = Visibility.Collapsed;
            return;
        }

        if (session is null)
        {
            StatusBar.Visibility = Visibility.Collapsed;
            return;
        }

        if (!ReferenceEquals(session, ActiveSession))
        {
            return;
        }

        StatusBar.Visibility = Visibility.Visible;

        var path = session.DocumentService.DocumentPath;
        string status;
        var actionable = false;
        if (session.IsExporting)
        {
            status = DesktopResources.Get("StatusExportingPng", "Exporting PNG…");
        }
        else if (session.ExportStatusMessage is { } exportStatus)
        {
            status = exportStatus;
        }
        else if (session.IsResuming || session.IsRetrying)
        {
            status = DesktopResources.Get("StatusResuming", "Resuming");
        }
        else if (session.IsUnloaded || session.IsUnloading ||
            session.IsSuspended || session.IsSuspensionChanging)
        {
            status = DesktopResources.Get("StatusSleeping", "Sleeping");
        }
        else if (session.ExternalFileState == ExternalFileState.Modified)
        {
            status = DesktopResources.Get(
                "StatusChangedOnDisk",
                "Changed on disk — Resolve");
            actionable = true;
        }
        else if (session.ExternalFileState == ExternalFileState.Deleted)
        {
            status = DesktopResources.Get(
                "StatusFileMissing",
                "File missing — Resolve");
            actionable = true;
        }
        else if (session.ExternalFileState == ExternalFileState.Moved)
        {
            status = DesktopResources.Get(
                "StatusFileMoved",
                "File moved — Resolve");
            actionable = true;
        }
        else if (session.IsDirty && session.RecoveryFailed)
        {
            status = DesktopResources.Get("StatusRecoveryFailed", "Recovery unavailable — Save As…");
            actionable = true;
        }
        else if (session.IsDirty)
        {
            status = session.RecoveryUpdatedAt is { } recoveredAt
                ? DesktopResources.Format(
                    "StatusUnsavedRecoveredFormat",
                    "Unsaved • recovered {0}",
                    recoveredAt.ToLocalTime().ToString("t"))
                : DesktopResources.Get("StatusUnsaved", "Unsaved changes");
        }
        else
        {
            status = path is null
                ? DesktopResources.Get("StatusNewDrawing", "New drawing")
                : DesktopResources.Get("StatusSaved", "Saved");
        }

        var document = path ?? session.DisplayName;
        StatusDocumentText.Text = document;
        ToolTipService.SetToolTip(StatusDocumentText, document);
        AutomationProperties.SetName(
            StatusDocumentText,
            DesktopResources.Format(
                "StatusDrawingAutomationFormat",
                "Drawing: {0}",
                document));

        StatusStateText.Text = status;
        StatusStateText.Visibility = actionable
            ? Visibility.Collapsed
            : Visibility.Visible;
        StatusActionButton.Content = status;
        StatusActionButton.Visibility = actionable
            ? Visibility.Visible
            : Visibility.Collapsed;
        AutomationProperties.SetName(StatusActionButton, status);
        AutomationProperties.SetHelpText(
            StatusActionButton,
            actionable
                ? DesktopResources.Get(
                    "StatusResolveHelpText",
                    "Open options for resolving the file conflict.")
                : string.Empty);
    }

    private async void OnStatusActionClick(object sender, RoutedEventArgs args)
    {
        if (CommandsBlocked) return;
        if (ActiveSession is { IsDirty: true, RecoveryFailed: true, ExternalFileState: ExternalFileState.None })
        {
            RequestSaveFromFileMenu(saveAs: true);
            return;
        }
        if (!settingsPageVisible &&
            ActiveSession is { ExternalFileState: not ExternalFileState.None } session)
        {
            await documents.ShowExternalFileConflictAsync(session);
        }
    }

    private static async Task OpenExternalUriAsync(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != "mailto"))
        {
            return;
        }

        try
        {
            if (!await Launcher.LaunchUriAsync(uri))
                DiagnosticLogService.Info("external_uri.not_launched");
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("external_uri.launch_failed", exception);
        }
    }

}
