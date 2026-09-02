using System.Diagnostics;
using System.Text.Json;
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
using Windows.Storage;
using Windows.System;
using Windows.UI.StartScreen;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow : Window
{
    private const string WebView2HelpUri =
        "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";

    private readonly string webAssetPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Web");
    private readonly List<DocumentSession> sessions = [];
    private readonly List<string> recentFiles = [];
    private readonly Queue<string> pendingActivatedFiles = [];
    private readonly SemaphoreSlim workspaceStateGate = new(1, 1);
    private readonly DispatcherTimer suspensionTimer = new();
    private readonly DesktopSettingsStore desktopSettingsStore = new();
    private readonly WorkspaceStateStore workspaceStateStore;
    private readonly RecoverySnapshotStore recoverySnapshotStore;
    private readonly bool runTabSmoke;
    private readonly bool runRecoverySmoke;
    private readonly bool verifyRecoverySmoke;
    private readonly bool runTitleBarSmoke;
    private readonly bool runSuspensionSmoke;
    private readonly bool performanceSuspendInactive;
    private readonly bool performanceUnloadInactive;
    private readonly int performanceTabCount;
    private readonly Stopwatch performanceStartup = Stopwatch.StartNew();
    private DesktopPreferences desktopPreferences = DesktopPreferences.Default;
    private DocumentSession? lastDocumentSession;
    private TabViewItem? settingsTabItem;
    private bool allowClose;
    private bool isWindowReady;
    private bool tabSmokeStarted;
    private bool titleBarRegistered;
    private bool titleBarRootSubscribed;
    private bool titleBarSmokeStarted;
    private bool performanceSmokeStarted;
    private bool suspensionSmokeStarted;
    private bool settingsPageVisible;
    private bool windowClosePromptOpen;
    private bool openPickerActive;
    private bool openingActivatedFiles;
    private bool jumpListUpdateQueued;
    private bool jumpListUpdateRunning;
    private bool restoringWorkspace = true;
    private bool recoverySmokeStarted;
    private bool resourcesDisposed;
    private int recoverySnapshotsSaved;
    private int recoveryTabsRestored;
    private int untitledSequence;
    private XamlRoot? titleBarXamlRoot;

    public MainWindow(
        bool runTabSmoke = false,
        string? workspaceStatePath = null,
        bool runRecoverySmoke = false,
        bool verifyRecoverySmoke = false,
        bool runTitleBarSmoke = false,
        int performanceTabCount = 0,
        bool runSuspensionSmoke = false,
        bool performanceSuspendInactive = false,
        bool performanceUnloadInactive = false)
    {
        InitializeComponent();
        desktopPreferences = desktopSettingsStore.Load();
        AppSettingsPage.LoadPreferences(desktopPreferences);
        AppSettingsPage.PreferencesChanged += OnPreferencesChanged;
        MainLayout.ActualThemeChanged += OnActualThemeChanged;
        ApplyDesktopTheme();
        InitializeTitleBar();
        var effectiveWorkspaceStatePath = workspaceStatePath ?? Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path,
                "workspace-state.json");
        workspaceStateStore = new WorkspaceStateStore(effectiveWorkspaceStatePath);
        recoverySnapshotStore = new RecoverySnapshotStore(Path.Combine(
            Path.GetDirectoryName(effectiveWorkspaceStatePath)!,
            $"{Path.GetFileNameWithoutExtension(effectiveWorkspaceStatePath)}.recovery"));
        this.runTabSmoke = runTabSmoke;
        this.runRecoverySmoke = runRecoverySmoke;
        this.verifyRecoverySmoke = verifyRecoverySmoke;
        this.runTitleBarSmoke = runTitleBarSmoke;
        this.performanceTabCount = performanceTabCount;
        this.runSuspensionSmoke = runSuspensionSmoke;
        this.performanceSuspendInactive = performanceSuspendInactive;
        this.performanceUnloadInactive = performanceUnloadInactive;
        PopulateRecentFilesMenu();
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
        AppWindow.Closing += OnAppWindowClosing;
        suspensionTimer.Interval = TimeSpan.FromSeconds(30);
        suspensionTimer.Tick += OnSuspensionTimerTick;
        suspensionTimer.Start();
        CreateTab();
        if (runTabSmoke)
        {
            CreateTab();
        }
        if (runRecoverySmoke)
        {
            CreateTab();
        }
        if (runTitleBarSmoke)
        {
            CreateTab();
        }
        if (runSuspensionSmoke)
        {
            CreateTab();
            CreateTab();
        }
        for (var index = 1; index < performanceTabCount; index++)
        {
            CreateTab();
        }
    }

    private void OnPreferencesChanged(
        object? sender,
        PreferencesChangedEventArgs args)
    {
        ApplyDesktopPreferences(args.Preferences);
    }

    private void InitializeTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(WindowDragRegion);
        titleBarRegistered = true;

        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        ApplyTitleBarTheme();
    }

    private void ApplyDesktopPreferences(DesktopPreferences preferences)
    {
        desktopPreferences = preferences;
        desktopSettingsStore.Save(preferences);
        ApplyDesktopTheme();

        if (!preferences.SuspendInactiveTabs)
        {
            foreach (var session in sessions.Where(session => session.IsSuspended))
            {
                ResumeSession(session);
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
        if (!session.IsReady || session.CoreWebView is null)
        {
            return;
        }

        session.CoreWebView.PostWebMessageAsJson(
            BridgeEventJson.Create(
                "app.themeChanged",
                new { theme = MainLayout.ActualTheme == ElementTheme.Dark ? "dark" : "light" }));
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

    private async void OnMainLayoutLoaded(object sender, RoutedEventArgs args)
    {
        await RestoreWorkspaceAsync();
        isWindowReady = true;
        if (ActiveSession is { } session)
        {
            _ = InitializeSessionAsync(session);
        }
        _ = DrainActivatedFilesAsync();
    }

    public void QueueActivatedFiles(IEnumerable<string> paths)
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

    private void OnFileDragOver(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(StandardDataFormats.StorageItems))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Copy;
        args.DragUIOverride.Caption = "Open drawing tabs";
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
            await ShowOpenErrorAsync("The dropped drawings could not be opened.");
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
                await OpenPathInTabAsync(path);
            }
        }
        finally
        {
            openingActivatedFiles = false;
        }
    }

    private async Task OpenPathInTabAsync(string path)
    {
        var canonicalPath = DesktopDocumentPath.Normalize(path);
        var existing = sessions.FirstOrDefault(session =>
            DesktopDocumentPath.Equals(
                session.DocumentService.DocumentPath,
                canonicalPath));
        if (existing is not null)
        {
            DocumentTabs.SelectedItem = existing.TabItem;
            if (!existing.IsUnloaded)
            {
                existing.Content.Editor.Focus(FocusState.Programmatic);
            }
            AddRecentFile(path);
            return;
        }

        try
        {
            var source = ActiveSession ?? sessions[0];
            var document = await source.DocumentService.OpenPathAsync(path);
            var target = ActiveSession is { IsDirty: false } active &&
                active.DocumentService.DocumentPath is null
                    ? active
                    : CreateTab();
            AttachDocumentToSession(target, document, select: true);
        }
        catch (BridgeProtocolException exception)
        {
            await ShowOpenErrorAsync(exception.Message);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await ShowOpenErrorAsync("The activated drawing could not be opened.");
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
        ConfigureEditorDropTarget(session, content.Editor);
        documentService.IsPathOwnedByAnotherSession = path =>
            sessions.Any(openSession =>
                !ReferenceEquals(openSession, session) &&
                DesktopDocumentPath.Equals(
                    openSession.DocumentService.DocumentPath,
                    path));
        session.Dispatcher = new BridgeDispatcher(
            documentService,
            () => OnAppReady(session),
            () => OnCloseReady(session),
            isDirty => OnDirtyChanged(session, isDirty),
            () => OnDocumentCreated(session),
            fileName => OnDocumentOpened(session, fileName),
            () => OnDocumentRecovered(session),
            content => OnRecoverySnapshotReceivedAsync(session, content),
            () => _ = CheckExternalFileStateAsync(session, showPrompt: true),
            () => OnCloseCancelled(session),
            () => CreateTab(),
            () => RequestOpenDocumentAsync(session),
            () => _ = RequestCloseSessionAsync(session),
            next => SelectAdjacentTab(session, next));
        session.Content.RetryRequested += (_, _) =>
            _ = RetrySessionAsync(session);
        session.Content.CloseRequested += (_, _) =>
            _ = RequestCloseSessionAsync(session);
        session.Content.WebView2HelpRequested += (_, _) =>
            _ = OpenExternalUriAsync(WebView2HelpUri);
        session.TabItem.PointerPressed += (_, args) =>
            OnTabPointerPressed(session, args);
        session.TabItem.ContextFlyout = CreateTabContextFlyout(session);

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

    private void ConfigureEditorDropTarget(
        DocumentSession session,
        WebView2 editor)
    {
        session.DetachEditorHandlers?.Invoke();
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
        TitleBarContainer.Opacity =
            args.WindowActivationState == WindowActivationState.Deactivated
                ? 0.82
                : 1;
        UpdateTitleBarInsets();

        if (isWindowReady &&
            !settingsPageVisible &&
            args.WindowActivationState != WindowActivationState.Deactivated &&
            ActiveSession is { } session)
        {
            if (session.IsUnloaded)
            {
                _ = WakeUnloadedSessionAsync(session);
            }
            else
            {
                _ = InitializeSessionAsync(session);
            }
            _ = CheckExternalFileStateAsync(session, showPrompt: true);
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (resourcesDisposed)
        {
            return;
        }

        resourcesDisposed = true;
        suspensionTimer.Stop();
        suspensionTimer.Tick -= OnSuspensionTimerTick;
        Activated -= OnWindowActivated;
        Closed -= OnWindowClosed;
        AppWindow.Closing -= OnAppWindowClosing;
        AppSettingsPage.PreferencesChanged -= OnPreferencesChanged;
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
            session.InactiveSince <= unloadCutoff && CanUnloadSession(session)).ToArray())
        {
            _ = UnloadSessionAsync(session, requireIdle: true);
        }

        var cutoff = now - TimeSpan.FromMinutes(5);
        foreach (var session in sessions.Where(session =>
            desktopPreferences.SuspendInactiveTabs &&
            session.InactiveSince <= cutoff && CanSuspendSession(session)).ToArray())
        {
            _ = SuspendSessionAsync(session, requireIdle: true);
        }
    }

    private bool CanSuspendSession(DocumentSession session) =>
        sessions.Contains(session) &&
        !ReferenceEquals(session, ActiveSession) &&
        session.IsReady &&
        !session.IsDirty &&
        !session.IsInitializing &&
        !session.IsUnloading &&
        !session.IsSuspended &&
        !session.IsSuspensionChanging &&
        !session.IsResuming &&
        session.CoreWebView is not null &&
        session.PendingDocumentLoad is null &&
        session.ExternalFileState is ExternalFileState.None &&
        !session.ExternalConflictPromptOpen &&
        !session.ClosePromptOpen &&
        session.CloseCompletion is null &&
        session.WindowCloseSaveCompletion is null;

    private bool CanUnloadSession(DocumentSession session) =>
        sessions.Contains(session) &&
        !ReferenceEquals(session, ActiveSession) &&
        session.IsReady &&
        !session.IsDirty &&
        !session.IsInitializing &&
        !session.IsUnloaded &&
        !session.IsUnloading &&
        !session.IsSuspensionChanging &&
        !session.IsResuming &&
        session.CoreWebView is not null &&
        session.PendingDocumentLoad is null &&
        session.ExternalFileState is ExternalFileState.None &&
        !session.ExternalConflictPromptOpen &&
        !session.ClosePromptOpen &&
        session.CloseCompletion is null &&
        session.WindowCloseSaveCompletion is null;

    private async Task<bool> UnloadSessionAsync(
        DocumentSession session,
        bool requireIdle)
    {
        if (!CanUnloadSession(session) ||
            (requireIdle && session.InactiveSince >
                DateTimeOffset.UtcNow - TimeSpan.FromMinutes(15)))
        {
            session.LastLifecycleFailure = "The tab was not eligible to unload.";
            return false;
        }

        session.LastLifecycleFailure = null;
        session.IsUnloading = true;
        try
        {
            string? cachedContent = null;
            if (session.DocumentService.DocumentPath is not null)
            {
                var stateBeforeRead = await session.DocumentService
                    .CheckExternalFileStateAsync();
                if (stateBeforeRead is not ExternalFileState.None)
                {
                    session.LastLifecycleFailure =
                        $"The file changed before unload ({stateBeforeRead}).";
                    session.ExternalFileState = stateBeforeRead;
                    session.InactiveSince = DateTimeOffset.UtcNow;
                    return false;
                }

                cachedContent = await session.DocumentService
                    .ReadActiveContentForHibernationAsync();
                var stateAfterRead = await session.DocumentService
                    .CheckExternalFileStateAsync();
                if (stateAfterRead is not ExternalFileState.None)
                {
                    session.LastLifecycleFailure =
                        $"The file changed while preparing to unload ({stateAfterRead}).";
                    session.ExternalFileState = stateAfterRead;
                    session.InactiveSince = DateTimeOffset.UtcNow;
                    return false;
                }
            }

            if (!CanUnloadSessionDuringTransition(session) ||
                session.Content.Visibility == Visibility.Visible)
            {
                session.LastLifecycleFailure =
                    "The tab became active or unsafe while preparing to unload.";
                return false;
            }

            if (session.CoreWebView!.IsSuspended)
            {
                session.CoreWebView.Resume();
                session.IsSuspended = false;
                await Task.Yield();
                if (!CanUnloadSessionDuringTransition(session) ||
                    session.Content.Visibility == Visibility.Visible)
                {
                    session.LastLifecycleFailure =
                        "The resumed tab became active or unsafe before disposal.";
                    return false;
                }
            }

            session.HibernatedContent = cachedContent;
            session.DetachEditorHandlers?.Invoke();
            session.DetachEditorHandlers = null;
            DetachWebView(session);
            session.Content.HibernateEditor();
            session.IsReady = false;
            session.IsSuspended = false;
            session.IsUnloaded = true;
            UpdateTabHeader(session);
            return true;
        }
        catch (Exception exception)
        {
            session.LastLifecycleFailure = exception.Message;
            Debug.WriteLine($"Tab unload failed: {exception}");
            return false;
        }
        finally
        {
            session.IsUnloading = false;
        }
    }

    private bool CanUnloadSessionDuringTransition(DocumentSession session) =>
        sessions.Contains(session) &&
        !ReferenceEquals(session, ActiveSession) &&
        session.IsReady &&
        !session.IsDirty &&
        !session.IsInitializing &&
        !session.IsUnloaded &&
        !session.IsResuming &&
        session.CoreWebView is not null &&
        session.PendingDocumentLoad is null &&
        session.ExternalFileState is ExternalFileState.None &&
        !session.ExternalConflictPromptOpen &&
        !session.ClosePromptOpen &&
        session.CloseCompletion is null &&
        session.WindowCloseSaveCompletion is null;

    private async Task WakeUnloadedSessionAsync(DocumentSession session)
    {
        if (!sessions.Contains(session) ||
            !session.IsUnloaded ||
            session.IsRestoringFromHibernation)
        {
            return;
        }

        session.IsRestoringFromHibernation = true;
        session.IsResuming = true;
        session.IsUnloaded = false;
        session.IsReady = false;
        session.IsInitializing = false;
        session.IsSuspended = false;
        session.CoreWebView = null;
        var editor = session.Content.RecreateEditor("Resuming drawing…");
        ConfigureEditorDropTarget(session, editor);

        if (session.HibernatedContent is { } content)
        {
            if (!session.DocumentService.StageActiveFileReload())
            {
                session.IsRestoringFromHibernation = false;
                session.IsResuming = false;
                session.Content.ShowFailure("This drawing could not be resumed.");
                return;
            }

            session.PendingDocumentLoad = new PendingEditorLoad(
                session.DisplayName,
                content,
                IsRecovery: false);
        }

        UpdateTabHeader(session);
        UpdateStatusBar(session);
        await InitializeSessionAsync(session);
    }

    private async Task<bool> SuspendSessionAsync(
        DocumentSession session,
        bool requireIdle)
    {
        if (!CanSuspendSession(session) ||
            (requireIdle && session.InactiveSince >
                DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5)))
        {
            return false;
        }

        session.IsSuspensionChanging = true;
        try
        {
            UpdateStatusBar(session);
            await Task.Yield();
            if (!CanSuspendSessionDuringTransition(session))
            {
                return false;
            }

            var suspended = await session.CoreWebView!.TrySuspendAsync();
            if (!suspended || !sessions.Contains(session))
            {
                return false;
            }

            if (ReferenceEquals(session, ActiveSession) ||
                session.Content.Visibility == Visibility.Visible)
            {
                if (session.CoreWebView.IsSuspended)
                {
                    session.CoreWebView.Resume();
                }
                return false;
            }

            session.IsSuspended = true;
            UpdateTabHeader(session);
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Tab suspension failed: {exception}");
            return false;
        }
        finally
        {
            session.IsSuspensionChanging = false;
        }
    }

    private bool CanSuspendSessionDuringTransition(DocumentSession session) =>
        sessions.Contains(session) &&
        !ReferenceEquals(session, ActiveSession) &&
        session.Content.Visibility != Visibility.Visible &&
        session.IsReady &&
        !session.IsDirty &&
        !session.IsUnloading &&
        session.CoreWebView is not null &&
        session.ExternalFileState is ExternalFileState.None &&
        session.PendingDocumentLoad is null;

    private void ResumeSession(DocumentSession session)
    {
        if (!session.IsSuspended || session.CoreWebView is null)
        {
            return;
        }

        session.IsResuming = true;
        try
        {
            session.CoreWebView.Resume();
            session.IsSuspended = false;
            UpdateTabHeader(session);
            UpdateStatusBar(session);
            _ = CompleteSessionResumeAsync(session);
        }
        catch (Exception exception)
        {
            session.IsResuming = false;
            Debug.WriteLine($"Tab resume failed: {exception}");
        }
    }

    private async Task CompleteSessionResumeAsync(DocumentSession session)
    {
        await Task.Yield();
        if (!sessions.Contains(session))
        {
            return;
        }

        session.IsResuming = false;
        session.InactiveSince = null;
        UpdateStatusBar(session);
    }

    private async Task InitializeSessionAsync(DocumentSession session)
    {
        if (!sessions.Contains(session) ||
            session.IsReady ||
            session.IsInitializing)
        {
            return;
        }

        session.IsInitializing = true;
        var webView = session.Content.Editor;
        Title = "Excalidraw Desktop — Initializing editor…";
        try
        {
            var entryPointPath = Path.Combine(webAssetPath, "index.html");
            if (!File.Exists(entryPointPath))
            {
                throw new FileNotFoundException(
                    "The desktop web bundle is missing. Run tools/Build-Desktop.ps1 first.",
                    entryPointPath);
            }

            await webView.EnsureCoreWebView2Async();
            ConfigureWebView(session, webView.CoreWebView2);
            webView.CoreWebView2.Navigate(session.TabOrigin.EntryPoint);
        }
        catch (Exception exception)
        {
            var runtimeMissing = IsWebView2RuntimeUnavailable(exception);
            session.LastLifecycleFailure = exception.Message;
            session.Content.ShowFailure(
                runtimeMissing
                    ? "Microsoft Edge WebView2 Runtime is required."
                    : "This drawing could not start.",
                runtimeMissing
                    ? "Install or repair the Evergreen WebView2 Runtime, then choose Retry. Your saved drawing is not changed."
                    : "Retry the editor. If the problem continues, repair WebView2 or rebuild the desktop app. Your saved drawing is not changed.",
                showWebView2Help: true);
            Title = "Excalidraw Desktop — Editor startup failed";
            Debug.WriteLine(exception);
        }
        finally
        {
            session.IsInitializing = false;
        }
    }

    private void ConfigureWebView(DocumentSession session, CoreWebView2 coreWebView)
    {
        DetachWebView(session);
        coreWebView.SetVirtualHostNameToFolderMapping(
            session.TabOrigin.Host,
            webAssetPath,
            CoreWebView2HostResourceAccessKind.DenyCors);

        coreWebView.Settings.AreBrowserAcceleratorKeysEnabled = true;
        coreWebView.Settings.AreDefaultContextMenusEnabled = true;
#if DEBUG
        coreWebView.Settings.AreDevToolsEnabled = true;
#else
        coreWebView.Settings.AreDevToolsEnabled = false;
#endif

        session.CoreWebView = coreWebView;
        TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs> navigationStarting =
            (_, args) => OnNavigationStarting(session, args);
        TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> navigationCompleted =
            (_, args) => OnNavigationCompleted(session, coreWebView, args);
        TypedEventHandler<CoreWebView2, CoreWebView2NewWindowRequestedEventArgs> newWindowRequested =
            (_, args) => OnNewWindowRequested(args);
        TypedEventHandler<CoreWebView2, CoreWebView2PermissionRequestedEventArgs> permissionRequested =
            (_, args) => OnPermissionRequested(session, args);
        TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs> webMessageReceived =
            (_, args) => OnWebMessageReceived(session, coreWebView, args);
        TypedEventHandler<CoreWebView2, CoreWebView2ProcessFailedEventArgs> processFailed =
            (_, args) => OnWebViewProcessFailed(session, coreWebView, args);

        coreWebView.NavigationStarting += navigationStarting;
        coreWebView.NavigationCompleted += navigationCompleted;
        coreWebView.NewWindowRequested += newWindowRequested;
        coreWebView.PermissionRequested += permissionRequested;
        coreWebView.WebMessageReceived += webMessageReceived;
        coreWebView.ProcessFailed += processFailed;
        session.DetachWebViewHandlers = () =>
        {
            coreWebView.NavigationStarting -= navigationStarting;
            coreWebView.NavigationCompleted -= navigationCompleted;
            coreWebView.NewWindowRequested -= newWindowRequested;
            coreWebView.PermissionRequested -= permissionRequested;
            coreWebView.WebMessageReceived -= webMessageReceived;
            coreWebView.ProcessFailed -= processFailed;
            coreWebView.ClearVirtualHostNameToFolderMapping(
                session.TabOrigin.Host);
        };
    }

    private static void DetachWebView(DocumentSession session)
    {
        var detachHandlers = session.DetachWebViewHandlers;
        session.DetachWebViewHandlers = null;
        session.CoreWebView = null;
        try
        {
            detachHandlers?.Invoke();
        }
        catch (Exception exception)
        {
            session.LastLifecycleFailure = exception.Message;
            Debug.WriteLine($"WebView cleanup failed: {exception}");
        }
    }

    private static bool IsWebView2RuntimeUnavailable(Exception exception) =>
        exception.HResult == unchecked((int)0x80070002) ||
        exception.Message.Contains(
            "WebView2 Runtime",
            StringComparison.OrdinalIgnoreCase);

    private async Task RetrySessionAsync(DocumentSession session)
    {
        if (!sessions.Contains(session) || session.IsInitializing)
        {
            return;
        }

        session.IsReady = false;
        session.IsSuspended = false;
        session.IsUnloaded = false;
        session.LastLifecycleFailure = null;
        session.DetachEditorHandlers?.Invoke();
        session.DetachEditorHandlers = null;
        DetachWebView(session);
        var editor = session.Content.RecreateEditor("Retrying editor…");
        ConfigureEditorDropTarget(session, editor);
        UpdateTabHeader(session);
        UpdateStatusBar(session);
        await InitializeSessionAsync(session);
    }

    private async void OnNavigationStarting(
        DocumentSession session,
        CoreWebView2NavigationStartingEventArgs args)
    {
        if (session.TabOrigin.Matches(args.Uri) || args.Uri == "about:blank")
        {
            return;
        }

        args.Cancel = true;
        await OpenExternalUriAsync(args.Uri);
    }

    private async void OnNavigationCompleted(
        DocumentSession session,
        CoreWebView2 coreWebView,
        CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!sessions.Contains(session) ||
            !ReferenceEquals(session.CoreWebView, coreWebView))
        {
            return;
        }

        if (!args.IsSuccess)
        {
            session.IsReady = false;
            session.LastLifecycleFailure = args.WebErrorStatus.ToString();
            session.Content.ShowFailure(
                $"This drawing could not load ({args.WebErrorStatus}).",
                "Check the local app installation and retry. If other WebView2 apps also fail, use WebView2 help to repair the runtime.",
                showWebView2Help: true);
            Title = "Excalidraw Desktop — Editor startup failed";
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(5));
        if (session.IsReady ||
            !sessions.Contains(session) ||
            !ReferenceEquals(session.CoreWebView, coreWebView))
        {
            return;
        }

#if DEBUG
        var diagnostics = await coreWebView.ExecuteScriptAsync(
            "JSON.stringify({readyState:document.readyState,rootChildren:document.getElementById('root')?.childElementCount??-1,origin:location.origin,transport:!!window.chrome?.webview})");
        Debug.WriteLine($"Editor bridge startup diagnostics: {diagnostics}");
        session.Content.ShowFailure(
            $"The editor loaded but its desktop bridge did not become ready. {diagnostics}");
#else
        session.Content.ShowFailure(
            "The editor loaded but its desktop bridge did not become ready.",
            "Retry the editor. If the problem continues, rebuild the local web assets and desktop app.");
#endif
        session.LastLifecycleFailure = "The desktop bridge did not become ready.";
        Title = "Excalidraw Desktop — Editor startup failed";
    }

    private void OnWebViewProcessFailed(
        DocumentSession session,
        CoreWebView2 coreWebView,
        CoreWebView2ProcessFailedEventArgs args)
    {
        if (!sessions.Contains(session) ||
            !ReferenceEquals(session.CoreWebView, coreWebView))
        {
            return;
        }

        session.IsReady = false;
        session.IsSuspended = false;
        session.LastLifecycleFailure = args.ProcessFailedKind.ToString();
        session.Content.ShowFailure(
            "The editor process stopped unexpectedly.",
            $"WebView2 reported {args.ProcessFailedKind}. Retry the editor. If failures continue, repair the WebView2 Runtime.",
            showWebView2Help: true);
        UpdateTabHeader(session);
        UpdateStatusBar(session);
        Title = "Excalidraw Desktop — Editor process failed";
    }

    private async void OnNewWindowRequested(
        CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        await OpenExternalUriAsync(args.Uri);
    }

    private void OnPermissionRequested(
        DocumentSession session,
        CoreWebView2PermissionRequestedEventArgs args)
    {
        args.State = args.PermissionKind == CoreWebView2PermissionKind.ClipboardRead &&
            session.TabOrigin.Matches(args.Uri)
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;
    }

    private async void OnWebMessageReceived(
        DocumentSession session,
        CoreWebView2 coreWebView,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!sessions.Contains(session) ||
            !ReferenceEquals(session.CoreWebView, coreWebView) ||
            !session.TabOrigin.Matches(args.Source))
        {
            return;
        }

        try
        {
            var message = BridgeMessageParser.Parse(args.WebMessageAsJson);
            await session.Dispatcher.DispatchAsync(coreWebView, message);
        }
        catch (BridgeProtocolException exception)
        {
            Debug.WriteLine($"Rejected bridge message ({exception.Code}): {exception.Message}");
        }
    }

    private void OnAppReady(DocumentSession session)
    {
        if (!sessions.Contains(session))
        {
            return;
        }

        session.IsReady = true;
        session.LastLifecycleFailure = null;
        session.Content.ShowReady();
        SendEditorTheme(session);
        UpdateWindowTitle();
        SendPendingDocumentLoad(session);
        if (session.IsRestoringFromHibernation &&
            session.PendingDocumentLoad is null)
        {
            CompleteHibernationRestore(session);
        }
        TryRunTabSmoke();
#if DEBUG
        TryRunTitleBarSmoke();
        TryRunPerformanceSmoke();
        TryRunSuspensionSmoke();
#endif
#if DEBUG
        TryRunRecoverySnapshotSmoke();
#endif
    }

    private async Task RequestOpenDocumentAsync(DocumentSession source)
    {
        if (openPickerActive || !sessions.Contains(source))
        {
            return;
        }

        openPickerActive = true;
        try
        {
            var document = await source.DocumentService.PickOpenDocumentAsync();
            if (document is null)
            {
                return;
            }

            var existing = sessions.FirstOrDefault(session =>
                DesktopDocumentPath.Equals(
                    session.DocumentService.DocumentPath,
                    document.CanonicalPath));
            if (existing is not null)
            {
                DocumentTabs.SelectedItem = existing.TabItem;
                if (!existing.IsUnloaded)
                {
                    existing.Content.Editor.Focus(FocusState.Programmatic);
                }
                return;
            }

            var target = ActiveSession is { IsDirty: false } active &&
                active.DocumentService.DocumentPath is null
                    ? active
                    : CreateTab();
            AttachDocumentToSession(target, document, select: true);
        }
        catch (BridgeProtocolException exception)
        {
            await ShowOpenErrorAsync(exception.Message);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await ShowOpenErrorAsync("The selected drawing could not be opened.");
        }
        finally
        {
            openPickerActive = false;
        }
    }

    private void AttachDocumentToSession(
        DocumentSession session,
        PickedDocument document,
        bool select)
    {
        session.DocumentService.StageOpen(document);
        WatchExternalFile(session, document.CanonicalPath);
        session.PendingDocumentLoad = new PendingEditorLoad(
            document.FileName,
            document.Content,
            IsRecovery: false);
        session.DisplayName = document.FileName;
        UpdateTabHeader(session);
        if (select)
        {
            DocumentTabs.SelectedItem = session.TabItem;
        }

        if (session.IsReady)
        {
            SendPendingDocumentLoad(session);
        }
    }

    private void WatchExternalFile(DocumentSession session, string path)
    {
        session.DetachExternalFileWatcher();
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            session.ExternalFileWatcher = null;
            return;
        }

        var uiDispatcherQueue = DispatcherQueue;
        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.FileName |
                NotifyFilters.LastWrite |
                NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler changed = (_, _) =>
            uiDispatcherQueue.TryEnqueue(() =>
                _ = CheckExternalFileStateAsync(session, showPrompt: false));
        RenamedEventHandler renamed = (_, _) =>
            uiDispatcherQueue.TryEnqueue(() =>
                _ = CheckExternalFileStateAsync(session, showPrompt: false));
        watcher.Changed += changed;
        watcher.Deleted += changed;
        watcher.Renamed += renamed;
        session.ExternalFileWatcher = watcher;
        session.ExternalFileChangedHandler = changed;
        session.ExternalFileRenamedHandler = renamed;
    }

    private void SendPendingDocumentLoad(DocumentSession session)
    {
        if (!session.IsReady ||
            session.CoreWebView is null ||
            session.PendingDocumentLoad is not { } document)
        {
            return;
        }

        session.CoreWebView.PostWebMessageAsJson(
            BridgeEventJson.Create(
                "document.loadRequested",
                new
                {
                    fileName = document.FileName,
                    content = document.Content,
                    isRecovery = document.IsRecovery,
                }));
    }

    private async Task ShowOpenErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = DocumentTabs.XamlRoot,
            Title = "Could not open drawing",
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private async Task RestoreWorkspaceAsync()
    {
        try
        {
            var state = await workspaceStateStore.LoadAsync();
            recentFiles.Clear();
            recentFiles.AddRange(state.RecentFiles
                .Select(DesktopDocumentPath.Normalize)
                .OfType<string>()
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10));

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
                                CloseSession(target);
                            }
                            continue;
                        }

                        var content = await recoverySnapshotStore.LoadAsync(recoveryId);
                        if (content is null)
                        {
                            if (!ReferenceEquals(target, initialSession))
                            {
                                CloseSession(target);
                            }
                            continue;
                        }

                        ExcalidrawDocumentValidator.Validate(content);
                        target.RecoveryId = recoveryId;
                        target.RecoveryUpdatedAt = savedTab.RecoveryUpdatedAt;
                        await target.DocumentService.RestoreActiveFileAsync(path);
                        if (target.DocumentService.DocumentPath is { } recoveredPath)
                        {
                            WatchExternalFile(target, recoveredPath);
                        }
                        var displayName = savedTab.DisplayName ??
                            (path is null ? "Recovered drawing" : Path.GetFileName(path));
                        AttachRecoveryToSession(target, displayName, content);
                    }
                    else
                    {
                        if (path is null || !File.Exists(path))
                        {
                            if (!ReferenceEquals(target, initialSession))
                            {
                                CloseSession(target);
                            }
                            continue;
                        }

                        var document = await initialSession.DocumentService.OpenPathAsync(path);
                        target.RecoveryId = savedTab.RecoveryId is { } cleanRecoveryId &&
                            Guid.TryParseExact(cleanRecoveryId, "N", out _)
                                ? cleanRecoveryId
                                : target.RecoveryId;
                        AttachDocumentToSession(target, document, select: false);
                    }
                    restoredSessions.Add(target);
                }
                catch (Exception exception)
                {
                    if (target is not null &&
                        !ReferenceEquals(target, initialSession))
                    {
                        CloseSession(target);
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
                    restoredSessions.FirstOrDefault(session =>
                        DesktopDocumentPath.Equals(
                            session.DocumentService.DocumentPath,
                            state.ActivePath)) ??
                    restoredSessions[0];
                DocumentTabs.SelectedItem = active.TabItem;
            }

            await recoverySnapshotStore.PruneExceptAsync(
                restoredSessions
                    .Where(session => session.IsDirty)
                    .Select(session => session.RecoveryId));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Workspace restore failed: {exception}");
        }
        finally
        {
            restoringWorkspace = false;
            PopulateRecentFilesMenu();
            QueuePersistWorkspace();
            QueueJumpListUpdate();
        }
    }

    private void AttachRecoveryToSession(
        DocumentSession session,
        string displayName,
        string content)
    {
        session.PendingDocumentLoad = new PendingEditorLoad(
            displayName,
            content,
            IsRecovery: true);
        session.DisplayName = displayName;
        session.IsDirty = true;
        UpdateTabHeader(session);
    }

    private async Task OpenRecentFileAsync(string path)
    {
        if (openPickerActive)
        {
            return;
        }

        openPickerActive = true;
        try
        {
            var canonicalPath = DesktopDocumentPath.Normalize(path);
            var existing = sessions.FirstOrDefault(session =>
                DesktopDocumentPath.Equals(
                    session.DocumentService.DocumentPath,
                    canonicalPath));
            if (existing is not null)
            {
                DocumentTabs.SelectedItem = existing.TabItem;
                AddRecentFile(path);
                return;
            }

            var source = ActiveSession ?? sessions[0];
            var document = await source.DocumentService.OpenPathAsync(path);
            var target = ActiveSession is { IsDirty: false } active &&
                active.DocumentService.DocumentPath is null
                    ? active
                    : CreateTab();
            AttachDocumentToSession(target, document, select: true);
        }
        catch (BridgeProtocolException exception)
        {
            if (exception.Code == "DocumentNotFound")
            {
                recentFiles.RemoveAll(recent =>
                    DesktopDocumentPath.Equals(recent, path));
                QueuePersistWorkspace();
                QueueJumpListUpdate();
            }
            await ShowOpenErrorAsync(exception.Message);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await ShowOpenErrorAsync("The recent drawing could not be opened.");
        }
        finally
        {
            openPickerActive = false;
        }
    }

    private async Task CheckExternalFileStateAsync(
        DocumentSession session,
        bool showPrompt)
    {
        if (!sessions.Contains(session) ||
            session.DocumentService.DocumentPath is null)
        {
            return;
        }

        try
        {
            var state = await session.DocumentService.CheckExternalFileStateAsync();
            if (!sessions.Contains(session))
            {
                return;
            }

            if (session.ExternalFileState != state &&
                !ReferenceEquals(session, ActiveSession))
            {
                session.InactiveSince = DateTimeOffset.UtcNow;
            }
            session.ExternalFileState = state;
            if (ReferenceEquals(session, ActiveSession))
            {
                UpdateWindowTitle();
            }
            if (showPrompt && state is not ExternalFileState.None)
            {
                await ShowExternalFileConflictAsync(session);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"External file check failed: {exception}");
        }
    }

    private async Task ShowExternalFileConflictAsync(DocumentSession session)
    {
        if (!sessions.Contains(session) ||
            session.ExternalFileState is ExternalFileState.None ||
            session.ExternalConflictPromptOpen)
        {
            return;
        }

        session.ExternalConflictPromptOpen = true;
        DocumentTabs.SelectedItem = session.TabItem;
        try
        {
            var requiresLocate = session.ExternalFileState is
                ExternalFileState.Deleted or ExternalFileState.Moved;
            var dialog = new ContentDialog
            {
                XamlRoot = DocumentTabs.XamlRoot,
                Title = requiresLocate
                    ? "Drawing file is missing"
                    : "Drawing changed outside the app",
                Content = requiresLocate
                    ? "The backing file was moved or deleted. Locate it, save this tab to a new file, or keep editing without overwriting anything."
                    : "Reload the disk version, save this tab to a different file, or keep editing. The existing file will not be overwritten automatically.",
                PrimaryButtonText = requiresLocate ? "Locate file" : "Reload",
                SecondaryButtonText = "Save As",
                CloseButtonText = "Keep editing",
                DefaultButton = ContentDialogButton.Close,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (requiresLocate)
                {
                    await LocateExternalFileAsync(session);
                }
                else
                {
                    await ReloadExternalFileAsync(session);
                }
            }
            else if (result == ContentDialogResult.Secondary)
            {
                session.CoreWebView?.PostWebMessageAsJson(
                    BridgeEventJson.Create(
                        "document.saveRequested",
                        new { reason = "externalConflict" }));
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"External conflict resolution failed: {exception}");
            await ShowOpenErrorAsync("The file conflict could not be resolved.");
        }
        finally
        {
            session.ExternalConflictPromptOpen = false;
            UpdateWindowTitle();
        }
    }

    private async Task ReloadExternalFileAsync(DocumentSession session)
    {
        var document = await session.DocumentService.ReloadActiveAsync();
        session.ExternalFileState = ExternalFileState.None;
        AttachDocumentToSession(session, document, select: true);
    }

    private async Task LocateExternalFileAsync(DocumentSession session)
    {
        var document = await session.DocumentService.PickOpenDocumentAsync();
        if (document is null)
        {
            return;
        }

        var existing = sessions.FirstOrDefault(openSession =>
            !ReferenceEquals(openSession, session) &&
            DesktopDocumentPath.Equals(
                openSession.DocumentService.DocumentPath,
                document.CanonicalPath));
        if (existing is not null)
        {
            DocumentTabs.SelectedItem = existing.TabItem;
            return;
        }

        session.ExternalFileState = ExternalFileState.None;
        AttachDocumentToSession(session, document, select: true);
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
                Text = "No recent drawings",
                IsEnabled = false,
            });
            return;
        }

        foreach (var path in recentFiles)
        {
            var item = new MenuFlyoutItem { Text = Path.GetFileName(path) };
            ToolTipService.SetToolTip(item, path);
            AutomationProperties.SetHelpText(item, path);
            item.Click += (_, _) => _ = OpenRecentFileAsync(path);
            RecentFilesMenu.Items.Add(item);
        }

        RecentFilesMenu.Items.Add(new MenuFlyoutSeparator());
        var clear = new MenuFlyoutItem { Text = "Clear recent drawings" };
        clear.Click += (_, _) =>
        {
            recentFiles.Clear();
            PopulateRecentFilesMenu();
            QueuePersistWorkspace();
            QueueJumpListUpdate();
        };
        RecentFilesMenu.Items.Add(clear);
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
        PopulateRecentFilesMenu();
        QueuePersistWorkspace();
        QueueJumpListUpdate();
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

    private void TryRunTabSmoke()
    {
#if DEBUG
        if (!runTabSmoke || tabSmokeStarted)
        {
            return;
        }

        if (sessions.FirstOrDefault(session => !session.IsReady) is { } pending)
        {
            DocumentTabs.SelectedItem = pending.TabItem;
            return;
        }

        tabSmokeStarted = true;
        _ = RunTabSmokeAsync();
#endif
    }

#if DEBUG
    private void TryRunTitleBarSmoke()
    {
        if (!runTitleBarSmoke || titleBarSmokeStarted)
        {
            return;
        }

        if (sessions.FirstOrDefault(session => !session.IsReady) is { } pending)
        {
            DocumentTabs.SelectedItem = pending.TabItem;
            return;
        }

        titleBarSmokeStarted = true;
        _ = RunTitleBarSmokeAsync();
    }

    private async Task RunTitleBarSmokeAsync()
    {
        string? cleanupProbePath = null;
        try
        {
            UpdateTitleBarInsets();
            var scale = MainLayout.XamlRoot?.RasterizationScale ?? 0;
            var expectedLeftInset = scale > 0
                ? AppWindow.TitleBar.LeftInset / scale
                : double.NaN;
            var expectedRightInset = scale > 0
                ? AppWindow.TitleBar.RightInset / scale
                : double.NaN;
            if (!ExtendsContentIntoTitleBar ||
                !titleBarRegistered ||
                scale <= 0 ||
                Math.Abs(TitleBarLeftInset.Width.Value - expectedLeftInset) > 0.75 ||
                Math.Abs(TitleBarRightInset.Width.Value - expectedRightInset) > 0.75 ||
                Math.Abs(TitleBarContainer.ActualHeight - 48) > 0.75 ||
                Math.Abs(DocumentTabs.ActualHeight - 48) > 0.75 ||
                WindowDragRegion.ActualWidth <= 0 ||
                !string.Equals(FileMenu.Title?.ToString(), "File", StringComparison.Ordinal) ||
                RecentFilesMenu.Items.Count == 0 ||
                !string.Equals(SaveMenuItem.Text, "Save", StringComparison.Ordinal) ||
                !string.Equals(SaveAsMenuItem.Text, "Save as…", StringComparison.Ordinal) ||
                SettingsButton.ActualWidth <= 0 ||
                sessions.Count != 2)
            {
                throw new InvalidOperationException(
                    "The title-bar layout was not initialized correctly.");
            }

            var requiredAccelerators = new[]
            {
                (VirtualKey.T, VirtualKeyModifiers.Control),
                (VirtualKey.N, VirtualKeyModifiers.Control),
                (VirtualKey.W, VirtualKeyModifiers.Control),
                (VirtualKey.O, VirtualKeyModifiers.Control),
                (VirtualKey.S, VirtualKeyModifiers.Control),
                (VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
                (VirtualKey.W, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
                (VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu),
                (VirtualKey.Tab, VirtualKeyModifiers.Control),
                (VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
            };
            if (requiredAccelerators.Any(required =>
                !DocumentTabs.KeyboardAccelerators.Any(accelerator =>
                    accelerator.Key == required.Item1 &&
                    accelerator.Modifiers == required.Item2)))
            {
                throw new InvalidOperationException(
                    "A required keyboard accelerator is missing from the title-bar tab strip.");
            }

            var originalTheme = MainLayout.RequestedTheme;
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                MainLayout.RequestedTheme = theme;
                await Task.Delay(100);
                var expectedTitleBarTheme = theme == ElementTheme.Dark
                    ? TitleBarTheme.Dark
                    : TitleBarTheme.Light;
                if (MainLayout.ActualTheme != theme ||
                    AppWindow.TitleBar.PreferredTheme != expectedTitleBarTheme)
                {
                    throw new InvalidOperationException(
                        $"The {theme} app-frame theme did not reach the title bar.");
                }
            }
            MainLayout.RequestedTheme = originalTheme;
            await Task.Delay(100);

            var first = sessions[0];
            var second = sessions[1];
            first.IsDirty = true;
            UpdateTabHeader(first);
            if (!AutomationProperties.GetName(first.TabItem).Contains(
                "unsaved changes",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The accessible tab name did not announce its dirty state.");
            }
            first.IsDirty = false;
            UpdateTabHeader(first);
            if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(DocumentTabs)) ||
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(WindowDragRegion)) ||
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(SettingsButton)) ||
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(first.Content.Editor)))
            {
                throw new InvalidOperationException(
                    "A title-bar or editor control is missing its accessible name.");
            }

            first.Content.ShowFailure(
                "Editor startup validation failure",
                "Retry, open WebView2 help, or close this tab.",
                showWebView2Help: true);
            if (!first.Content.IsFailureVisible ||
                !first.Content.IsWebView2HelpVisible)
            {
                throw new InvalidOperationException(
                    "The editor failure actions were not displayed.");
            }
            var previousCoreWebView = first.CoreWebView;
            await RetrySessionAsync(first);
            var retryDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
            while (!first.IsReady && DateTimeOffset.UtcNow < retryDeadline)
            {
                await Task.Delay(100);
            }
            if (!first.IsReady ||
                first.CoreWebView is null ||
                ReferenceEquals(first.CoreWebView, previousCoreWebView) ||
                first.DetachWebViewHandlers is null ||
                first.DetachEditorHandlers is null ||
                first.Content.IsFailureVisible)
            {
                throw new InvalidOperationException(
                    "The failed editor did not retry with a fresh WebView.");
            }

            ShowSettingsPage();
            await Task.Yield();
            if (!settingsPageVisible ||
                settingsTabItem is null ||
                !DocumentTabs.TabItems.Contains(settingsTabItem) ||
                FindSession(settingsTabItem) is not null ||
                AppSettingsPage.Visibility != Visibility.Visible ||
                EditorHost.Visibility != Visibility.Collapsed ||
                Title != "Settings — Excalidraw Desktop")
            {
                throw new InvalidOperationException(
                    "The native Settings tab did not replace the editor surface correctly.");
            }
            HideSettingsPage();
            if (settingsPageVisible ||
                settingsTabItem is not null ||
                AppSettingsPage.Visibility != Visibility.Collapsed ||
                EditorHost.Visibility != Visibility.Visible)
            {
                throw new InvalidOperationException(
                    "The native Settings tab was not hidden correctly.");
            }

            DocumentTabs.TabItems.Remove(first.TabItem);
            DocumentTabs.TabItems.Insert(1, first.TabItem);
            SynchronizeSessionOrder();
            DocumentTabs.SelectedItem = first.TabItem;
            cleanupProbePath = Path.Combine(
                AppContext.BaseDirectory,
                "titlebar-resource-cleanup.tmp");
            await File.WriteAllTextAsync(cleanupProbePath, "cleanup");
            WatchExternalFile(second, cleanupProbePath);
            if (second.ExternalFileWatcher is null ||
                second.ExternalFileChangedHandler is null ||
                second.ExternalFileRenamedHandler is null)
            {
                throw new InvalidOperationException(
                    "The file-watcher cleanup probe was not attached.");
            }

            if (sessions.Count != 2 ||
                !ReferenceEquals(sessions[1], first) ||
                !ReferenceEquals(ActiveSession, first) ||
                !await RequestCloseSessionAsync(second) ||
                sessions.Count != 1 ||
                second.CoreWebView is not null ||
                second.DetachWebViewHandlers is not null ||
                second.DetachEditorHandlers is not null ||
                second.ExternalFileWatcher is not null ||
                second.ExternalFileChangedHandler is not null ||
                second.ExternalFileRenamedHandler is not null ||
                second.Content.HasEditor ||
                second.LastLifecycleFailure is not null)
            {
                throw new InvalidOperationException(
                    "Tab interactions or resource cleanup regressed in the custom title bar.");
            }

            Title = "Excalidraw Desktop — Title bar smoke passed";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Title = "Excalidraw Desktop — Title bar smoke failed";
        }
        finally
        {
            if (cleanupProbePath is not null && File.Exists(cleanupProbePath))
            {
                File.Delete(cleanupProbePath);
            }
        }
    }

    private void TryRunPerformanceSmoke()
    {
        if (performanceTabCount <= 0 || performanceSmokeStarted)
        {
            return;
        }

        if (sessions.FirstOrDefault(session => !session.IsReady) is { } pending)
        {
            DocumentTabs.SelectedItem = pending.TabItem;
            return;
        }

        performanceSmokeStarted = true;
        _ = RunPerformanceSmokeAsync();
    }

    private async Task RunPerformanceSmokeAsync()
    {
        try
        {
            if (sessions.Count != performanceTabCount ||
                sessions.Select(session => session.TabOrigin.Host).Distinct().Count() !=
                    performanceTabCount)
            {
                throw new InvalidOperationException(
                    "The performance run did not create isolated ready tabs.");
            }

            var startupMilliseconds = performanceStartup.Elapsed.TotalMilliseconds;
            var switchDurations = new List<double>();
            for (var pass = 0; pass < 3; pass++)
            {
                foreach (var session in sessions)
                {
                    var started = Stopwatch.GetTimestamp();
                    DocumentTabs.SelectedItem = session.TabItem;
                    await Task.Yield();
                    switchDurations.Add(
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
            }

            var suspendedTabCount = 0;
            var unloadedTabCount = 0;
            if ((performanceSuspendInactive || performanceUnloadInactive) &&
                sessions.Count > 1)
            {
                DocumentTabs.SelectedItem = sessions[^1].TabItem;
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                foreach (var session in sessions.Where(session =>
                    !ReferenceEquals(session, ActiveSession)).ToArray())
                {
                    if (performanceUnloadInactive)
                    {
                        if (await UnloadSessionAsync(session, requireIdle: false))
                        {
                            unloadedTabCount++;
                        }
                    }
                    else if (await SuspendSessionAsync(session, requireIdle: false))
                    {
                        suspendedTabCount++;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var result = JsonSerializer.Serialize(new
            {
                tabCount = performanceTabCount,
                startupMilliseconds,
                averageSwitchMilliseconds = switchDurations.Average(),
                maximumSwitchMilliseconds = switchDurations.Max(),
                workingSetBytes = process.WorkingSet64,
                privateMemoryBytes = process.PrivateMemorySize64,
                uniqueOrigins = sessions.Count,
                suspendInactive = performanceSuspendInactive,
                suspendedTabCount,
                unloadInactive = performanceUnloadInactive,
                unloadedTabCount,
            }, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "performance-smoke-result.json"),
                result);
            Title = "Excalidraw Desktop — Performance smoke passed";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Title = "Excalidraw Desktop — Performance smoke failed";
        }
    }

    private void TryRunSuspensionSmoke()
    {
        if (!runSuspensionSmoke || suspensionSmokeStarted)
        {
            return;
        }

        if (sessions.FirstOrDefault(session => !session.IsReady) is { } pending)
        {
            DocumentTabs.SelectedItem = pending.TabItem;
            return;
        }

        suspensionSmokeStarted = true;
        _ = RunSuspensionSmokeAsync();
    }

    private async Task RunSuspensionSmokeAsync()
    {
        try
        {
            if (sessions.Count != 3 || ActiveSession is not { } active)
            {
                throw new InvalidOperationException(
                    "The suspension smoke test requires three ready tabs.");
            }

            if (sessions.Any(session =>
                session.Content.Editor.ActualWidth < 100 ||
                session.Content.Editor.ActualHeight < 100))
            {
                throw new InvalidOperationException(
                    "A ready editor WebView was not laid out at a visible size.");
            }

            var inactive = sessions.Where(session =>
                !ReferenceEquals(session, active)).ToArray();
            var sleeping = inactive[0];
            var unloading = inactive[1];
            sleeping.InactiveSince = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(6);
            if (!await SuspendSessionAsync(sleeping, requireIdle: true) ||
                !sleeping.IsSuspended ||
                sleeping.CoreWebView?.IsSuspended != true)
            {
                throw new InvalidOperationException(
                    "The inactive clean tab did not suspend.");
            }

            DocumentTabs.SelectedItem = sleeping.TabItem;
            await Task.Yield();
            if (sleeping.IsSuspended ||
                sleeping.CoreWebView?.IsSuspended == true ||
                !ReferenceEquals(ActiveSession, sleeping) ||
                sleeping.CoreWebView is null ||
                !sleeping.TabOrigin.Matches(sleeping.CoreWebView.Source))
            {
                throw new InvalidOperationException(
                    "The sleeping tab did not resume when selected.");
            }

            unloading.InactiveSince = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(16);
            if (!await SuspendSessionAsync(unloading, requireIdle: false) ||
                !unloading.IsSuspended ||
                !await UnloadSessionAsync(unloading, requireIdle: true) ||
                !unloading.IsUnloaded ||
                unloading.CoreWebView is not null ||
                unloading.DetachWebViewHandlers is not null ||
                unloading.DetachEditorHandlers is not null ||
                unloading.Content.HasEditor ||
                unloading.LastLifecycleFailure is not null)
            {
                throw new InvalidOperationException(
                    $"The inactive clean tab did not fully unload. {unloading.LastLifecycleFailure}");
            }

            DocumentTabs.SelectedItem = unloading.TabItem;
            var resumeDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
            while ((!unloading.IsReady || unloading.IsRestoringFromHibernation) &&
                DateTimeOffset.UtcNow < resumeDeadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            if (!unloading.IsReady ||
                unloading.IsUnloaded ||
                unloading.IsRestoringFromHibernation ||
                !ReferenceEquals(ActiveSession, unloading) ||
                unloading.CoreWebView is null ||
                unloading.DetachWebViewHandlers is null ||
                unloading.DetachEditorHandlers is null ||
                !unloading.Content.HasEditor ||
                !unloading.TabOrigin.Matches(unloading.CoreWebView.Source))
            {
                throw new InvalidOperationException(
                    "The unloaded tab did not recreate its isolated editor.");
            }

            Title = "Excalidraw Desktop — Suspension smoke passed";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Title = $"Excalidraw Desktop — Suspension smoke failed: {exception.Message}";
        }
    }
#endif

#if DEBUG
    private async Task RunTabSmokeAsync()
    {
        const string storageKey = "__excalidrawDesktopTabSmoke";
        try
        {
            if (sessions.Count != 2 ||
                sessions.Select(session => session.TabOrigin.Host).Distinct().Count() != 2 ||
                sessions.Any(session => session.CoreWebView is null))
            {
                throw new InvalidOperationException(
                    "The tab smoke test did not create two isolated editor sessions.");
            }

            await sessions[0].CoreWebView!.ExecuteScriptAsync(
                $"localStorage.setItem('{storageKey}', 'first')");
            await sessions[1].CoreWebView!.ExecuteScriptAsync(
                $"localStorage.setItem('{storageKey}', 'second')");

            var firstJson = await sessions[0].CoreWebView!.ExecuteScriptAsync(
                $"localStorage.getItem('{storageKey}')");
            var secondJson = await sessions[1].CoreWebView!.ExecuteScriptAsync(
                $"localStorage.getItem('{storageKey}')");
            var first = JsonSerializer.Deserialize<string>(firstJson);
            var second = JsonSerializer.Deserialize<string>(secondJson);
            if (first != "first" || second != "second")
            {
                throw new InvalidOperationException(
                    "WebView2 browser storage leaked between document-tab origins.");
            }

            await sessions[0].CoreWebView!.ExecuteScriptAsync(
                $"localStorage.removeItem('{storageKey}')");
            await sessions[1].CoreWebView!.ExecuteScriptAsync(
                $"localStorage.removeItem('{storageKey}')");

            var originallyFirst = sessions[0];
            var originallySecond = sessions[1];
            DocumentTabs.TabItems.Remove(originallySecond.TabItem);
            DocumentTabs.TabItems.Insert(0, originallySecond.TabItem);
            SynchronizeSessionOrder();
            if (!ReferenceEquals(sessions[0], originallySecond) ||
                !ReferenceEquals(
                    WorkspaceTabOperations.Adjacent(
                        GetOrderedSessions(),
                        originallySecond,
                        next: true),
                    originallyFirst))
            {
                throw new InvalidOperationException(
                    "The tab workspace did not adopt the reordered visual tab sequence.");
            }

            DocumentTabs.SelectedItem = sessions[1].TabItem;
            Title = "Excalidraw Desktop — Tab smoke passed";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Title = "Excalidraw Desktop — Tab smoke failed";
        }
    }
#endif

    private void OnCloseReady(DocumentSession session)
    {
        if (!sessions.Contains(session) || session.IsDirty)
        {
            return;
        }

        if (session.WindowCloseSaveCompletion is { } saveCompletion)
        {
            session.WindowCloseSaveCompletion = null;
            saveCompletion.TrySetResult(true);
            return;
        }

        if (session.CloseAfterSave)
        {
            var completion = session.CloseCompletion;
            session.CloseAfterSave = false;
            session.CloseCompletion = null;
            CloseSession(session);
            completion?.TrySetResult(true);
        }
    }

    private void OnCloseCancelled(DocumentSession session)
    {
        var saveCompletion = session.WindowCloseSaveCompletion;
        var completion = session.CloseCompletion;
        session.WindowCloseSaveCompletion = null;
        session.CloseAfterSave = false;
        session.CloseCompletion = null;
        saveCompletion?.TrySetResult(false);
        completion?.TrySetResult(false);
    }

    private void OnDirtyChanged(DocumentSession session, bool isDirty)
    {
        if (!sessions.Contains(session))
        {
            return;
        }

        session.IsDirty = isDirty;
        if (!ReferenceEquals(session, ActiveSession))
        {
            session.InactiveSince = DateTimeOffset.UtcNow;
        }
        if (!isDirty)
        {
            session.RecoveryUpdatedAt = null;
            _ = DeleteRecoverySnapshotAsync(session);
        }
        UpdateTabHeader(session);
        UpdateWindowTitle();
        QueuePersistWorkspace();
    }

    private void OnDocumentCreated(DocumentSession session)
    {
        _ = DeleteRecoverySnapshotAsync(session);
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
        if (session.IsRestoringFromHibernation)
        {
            session.DisplayName = fileName;
            CompleteHibernationRestore(session);
            UpdateWindowTitle();
            if (session.DocumentService.DocumentPath is { } restoredPath)
            {
                AddRecentFile(restoredPath);
                _ = CheckExternalFileStateAsync(session, showPrompt: false);
            }
            return;
        }

        session.RecoveryUpdatedAt = null;
        _ = DeleteRecoverySnapshotAsync(session);
        session.ExternalFileState = ExternalFileState.None;
        session.DisplayName = fileName;
        UpdateTabHeader(session);
        UpdateWindowTitle();
        if (session.DocumentService.DocumentPath is { } path)
        {
            WatchExternalFile(session, path);
            AddRecentFile(path);
            _ = CheckExternalFileStateAsync(session, showPrompt: false);
        }
    }

    private void CompleteHibernationRestore(DocumentSession session)
    {
        session.IsRestoringFromHibernation = false;
        session.IsResuming = false;
        session.HibernatedContent = null;
        session.InactiveSince = null;
        UpdateTabHeader(session);
        UpdateStatusBar(session);
    }

    private void OnDocumentRecovered(DocumentSession session)
    {
        if (!sessions.Contains(session))
        {
            return;
        }

        session.PendingDocumentLoad = null;
        session.IsDirty = true;
        UpdateTabHeader(session);
        UpdateWindowTitle();
#if DEBUG
        if (verifyRecoverySmoke)
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

    private async Task OnRecoverySnapshotReceivedAsync(
        DocumentSession session,
        string content)
    {
        if (!sessions.Contains(session) || !session.IsDirty)
        {
            return;
        }

        await session.RecoveryGate.WaitAsync();
        try
        {
            if (sessions.Contains(session) && session.IsDirty)
            {
                await recoverySnapshotStore.SaveAsync(session.RecoveryId, content);
                session.RecoveryUpdatedAt = DateTimeOffset.UtcNow;
                QueuePersistWorkspace();
                if (ReferenceEquals(session, ActiveSession))
                {
                    UpdateWindowTitle();
                }
#if DEBUG
                if (runRecoverySmoke)
                {
                    recoverySnapshotsSaved++;
                    if (recoverySnapshotsSaved == sessions.Count)
                    {
                        await PersistWorkspaceAsync();
                        Title = "Excalidraw Desktop — Recovery snapshot saved";
                    }
                }
#endif
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Recovery snapshot failed: {exception}");
        }
        finally
        {
            session.RecoveryGate.Release();
        }
    }

#if DEBUG
    private void TryRunRecoverySnapshotSmoke()
    {
        if (!runRecoverySmoke || recoverySmokeStarted)
        {
            return;
        }

        if (sessions.FirstOrDefault(session => !session.IsReady) is { } pending)
        {
            DocumentTabs.SelectedItem = pending.TabItem;
            return;
        }

        recoverySmokeStarted = true;
        foreach (var session in sessions)
        {
            _ = RunRecoverySnapshotSmokeAsync(session);
        }
    }

    private static async Task RunRecoverySnapshotSmokeAsync(DocumentSession session)
    {
        if (session.CoreWebView is null)
        {
            return;
        }

        var dirtyMessage = BridgeEventJson.Create(
            "document.dirtyChanged",
            new { isDirty = true });
        var snapshotMessage = BridgeEventJson.Create(
            "document.recoverySnapshot",
            new
            {
                content = "{\"type\":\"excalidraw\",\"version\":2,\"elements\":[],\"appState\":{},\"files\":{}}",
            });
        await session.CoreWebView.ExecuteScriptAsync(
            $"window.chrome.webview.postMessage({dirtyMessage})");
        await session.CoreWebView.ExecuteScriptAsync(
            $"window.chrome.webview.postMessage({snapshotMessage})");
    }
#endif

    private async Task DeleteRecoverySnapshotAsync(DocumentSession session)
    {
        var recoveryId = session.RecoveryId;
        await session.RecoveryGate.WaitAsync();
        try
        {
            await recoverySnapshotStore.DeleteAsync(recoveryId);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Recovery snapshot cleanup failed: {exception}");
        }
        finally
        {
            session.RecoveryGate.Release();
        }
    }

    private void OnAddTabButtonClick(TabView sender, object args)
    {
        CreateTab();
    }

    private void OnFileNewTabClick(object sender, RoutedEventArgs args)
    {
        CreateTab();
    }

    private void OnFileOpenClick(object sender, RoutedEventArgs args)
    {
        if (ActiveSession is { } session)
        {
            _ = RequestOpenDocumentAsync(session);
        }
    }

    private void OnFileSaveClick(object sender, RoutedEventArgs args)
    {
        RequestSaveFromFileMenu(saveAs: false);
    }

    private void OnFileSaveAsClick(object sender, RoutedEventArgs args)
    {
        RequestSaveFromFileMenu(saveAs: true);
    }

    private void RequestSaveFromFileMenu(bool saveAs)
    {
        if (ActiveSession is not { IsReady: true, CoreWebView: { } coreWebView } session ||
            session.IsResuming ||
            session.IsRestoringFromHibernation)
        {
            return;
        }

        coreWebView.PostWebMessageAsJson(
            BridgeEventJson.Create(
                "document.saveRequested",
                new { reason = saveAs ? "saveAs" : "save" }));
    }

    private async void OnFileSaveAllClick(object sender, RoutedEventArgs args)
    {
        await SaveAllFromFileMenuAsync();
    }

    private async Task SaveAllFromFileMenuAsync()
    {
        var dirtySessions = sessions.Where(session => session.IsDirty).ToArray();
        if (dirtySessions.Length == 0)
        {
            return;
        }

        var originallyActive = ActiveSession;
        await SaveAllForWindowCloseAsync(dirtySessions);
        if (originallyActive is not null && sessions.Contains(originallyActive))
        {
            DocumentTabs.SelectedItem = originallyActive.TabItem;
        }
    }

    private void OnFileCloseTabClick(object sender, RoutedEventArgs args)
    {
        if (settingsTabItem is not null &&
            ReferenceEquals(DocumentTabs.SelectedItem, settingsTabItem))
        {
            HideSettingsPage();
            return;
        }

        if (ActiveSession is { } session)
        {
            _ = RequestCloseSessionAsync(session);
        }
    }

    private void OnFileCloseWindowClick(object sender, RoutedEventArgs args)
    {
        Close();
    }

    private void OnFileExitClick(object sender, RoutedEventArgs args)
    {
        Close();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs args)
    {
        ShowSettingsPage();
    }

    private void OnSettingsButtonClick(object sender, RoutedEventArgs args)
    {
        ShowSettingsPage();
    }

    private void ShowSettingsPage()
    {
        AppSettingsPage.LoadPreferences(desktopPreferences);
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
                Text = "Settings",
                VerticalAlignment = VerticalAlignment.Center,
            });
            settingsTabItem = new TabViewItem
            {
                Header = header,
                IsClosable = true,
            };
            AutomationProperties.SetName(settingsTabItem, "Settings");
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

    private void OnNewTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CreateTab();
    }

    private void OnCloseTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (settingsTabItem is not null &&
            ReferenceEquals(DocumentTabs.SelectedItem, settingsTabItem))
        {
            HideSettingsPage();
            return;
        }

        if (ActiveSession is { } session)
        {
            _ = RequestCloseSessionAsync(session);
        }
    }

    private void OnOpenAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ActiveSession is { } session)
        {
            _ = RequestOpenDocumentAsync(session);
        }
    }

    private void OnSaveAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        RequestSaveFromFileMenu(saveAs: false);
    }

    private void OnSaveAsAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        RequestSaveFromFileMenu(saveAs: true);
    }

    private void OnCloseWindowAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    private void OnSaveAllAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = SaveAllFromFileMenuAsync();
    }

    private void OnNextTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
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
        if (DocumentTabs.SelectedItem is TabViewItem tab)
        {
            SelectAdjacentTabItem(tab, next: false);
        }
    }

    private async void OnTabCloseRequested(
        TabView sender,
        TabViewTabCloseRequestedEventArgs args)
    {
        if (settingsTabItem is not null && ReferenceEquals(args.Tab, settingsTabItem))
        {
            HideSettingsPage();
            return;
        }

        if (FindSession(args.Tab) is { } session)
        {
            await RequestCloseSessionAsync(session);
        }
    }

    private void OnTabPointerPressed(
        DocumentSession session,
        PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(session.TabItem).Properties.IsMiddleButtonPressed)
        {
            return;
        }

        args.Handled = true;
        _ = RequestCloseSessionAsync(session);
    }

    private void OnTabDragCompleted(
        TabView sender,
        TabViewTabDragCompletedEventArgs args)
    {
        SynchronizeSessionOrder();
    }

    private void OnTabSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
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
                    _ = WakeUnloadedSessionAsync(session);
                }
                else
                {
                    ResumeSession(session);
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
                _ = InitializeSessionAsync(activeSession);
            }
            _ = CheckExternalFileStateAsync(activeSession, showPrompt: true);
        }
        QueuePersistWorkspace();
    }

    private async Task<bool> RequestCloseSessionAsync(DocumentSession session)
    {
        if (!sessions.Contains(session) || session.ClosePromptOpen)
        {
            return false;
        }

        if (!session.IsDirty)
        {
            CloseSession(session);
            return true;
        }

        session.ClosePromptOpen = true;
        DocumentTabs.SelectedItem = session.TabItem;
        try
        {
            var decision = await session.DocumentService.PromptToSaveBeforeCloseAsync();
            if (decision == CloseDecision.Discard)
            {
                CloseSession(session);
                return true;
            }

            if (decision == CloseDecision.Save && session.CoreWebView is not null)
            {
                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                session.CloseCompletion = completion;
                session.CloseAfterSave = true;
                session.CoreWebView.PostWebMessageAsJson(
                    BridgeEventJson.Create(
                        "document.saveRequested",
                        new { reason = "close" }));
                return await completion.Task;
            }

            return false;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            session.CloseAfterSave = false;
            session.CloseCompletion?.TrySetResult(false);
            session.CloseCompletion = null;
            return false;
        }
        finally
        {
            session.ClosePromptOpen = false;
        }
    }

    private void CloseSession(DocumentSession session)
    {
        if (!sessions.Remove(session))
        {
            return;
        }

        DocumentTabs.TabItems.Remove(session.TabItem);
        EditorHost.Children.Remove(session.Content);
        _ = DeleteRecoverySnapshotAsync(session);
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
        var newTab = new MenuFlyoutItem { Text = "New Tab" };
        newTab.Click += (_, _) => CreateTab();

        var copyPath = new MenuFlyoutItem { Text = "Copy Path" };
        AutomationProperties.SetAutomationId(copyPath, "CopyTabPath");
        copyPath.Click += (_, _) => _ = CopyDocumentPathAsync(session);

        var reveal = new MenuFlyoutItem { Text = "Reveal in Explorer" };
        AutomationProperties.SetAutomationId(reveal, "RevealTabInExplorer");
        reveal.Click += (_, _) => _ = RevealDocumentInExplorerAsync(session);

        var close = new MenuFlyoutItem { Text = "Close Tab" };
        close.Click += (_, _) => _ = RequestCloseSessionAsync(session);

        var closeOthers = new MenuFlyoutItem { Text = "Close Other Tabs" };
        closeOthers.Click += (_, _) => _ = CloseSessionsAsync(
            WorkspaceTabOperations.OtherTabs(GetOrderedSessions(), session));

        var closeRight = new MenuFlyoutItem { Text = "Close Tabs to the Right" };
        closeRight.Click += (_, _) => _ = CloseSessionsAsync(
            WorkspaceTabOperations.TabsToRight(GetOrderedSessions(), session));

        flyout.Opening += (_, _) =>
        {
            var path = session.DocumentService.DocumentPath;
            copyPath.IsEnabled = path is not null;
            reveal.IsEnabled = path is not null && File.Exists(path);
        };

        flyout.Items.Add(newTab);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(copyPath);
        flyout.Items.Add(reveal);
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
                "The drawing path could not be copied.");
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
                    "The drawing is no longer at its saved location.");
                return;
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            var options = new FolderLauncherOptions();
            options.ItemsToSelect.Add(file);
            if (!await Launcher.LaunchFolderAsync(folder, options))
            {
                await ShowFileLocationErrorAsync(
                    "File Explorer could not show this drawing.");
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await ShowFileLocationErrorAsync(
                "The drawing is no longer at its saved location.");
        }
    }

    private async Task ShowFileLocationErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = DocumentTabs.XamlRoot,
            Title = "File location unavailable",
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private async Task CloseSessionsAsync(IEnumerable<DocumentSession> targets)
    {
        foreach (var session in targets.ToArray())
        {
            if (sessions.Contains(session) &&
                !await RequestCloseSessionAsync(session))
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

        var dirtySessions = sessions.Where(session => session.IsDirty).ToList();
        if (dirtySessions.Count == 0)
        {
            args.Cancel = true;
            if (windowClosePromptOpen)
            {
                return;
            }

            windowClosePromptOpen = true;
            try
            {
                await PersistWorkspaceAsync();
                await recoverySnapshotStore.PruneExceptAsync(Array.Empty<string>());
                allowClose = true;
                Close();
            }
            finally
            {
                windowClosePromptOpen = false;
            }
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
            var names = string.Join(
                Environment.NewLine,
                dirtySessions.Select(session => $"• {session.DisplayName}"));
            var reviewRequested = false;
            var reviewButton = new Button
            {
                Content = "Review tabs individually",
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetName(
                reviewButton,
                "Review unsaved tabs individually");
            var content = new StackPanel { Spacing = 16 };
            content.Children.Add(new TextBlock
            {
                Text = $"{dirtySessions.Count} drawing(s) have unsaved changes:\n\n{names}",
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(reviewButton);
            var dialog = new ContentDialog
            {
                XamlRoot = DocumentTabs.XamlRoot,
                Title = "Unsaved drawings",
                Content = content,
                PrimaryButtonText = "Save all",
                SecondaryButtonText = "Discard all",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            reviewButton.Click += (_, _) =>
            {
                reviewRequested = true;
                dialog.Hide();
            };

            var result = await dialog.ShowAsync();
            if (reviewRequested)
            {
                DocumentTabs.SelectedItem = dirtySessions[0].TabItem;
            }
            else if (result == ContentDialogResult.Primary)
            {
                if (await SaveAllForWindowCloseAsync(dirtySessions))
                {
                    await PersistWorkspaceAsync();
                    await recoverySnapshotStore.PruneExceptAsync(Array.Empty<string>());
                    allowClose = true;
                    Close();
                }
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await PersistWorkspaceAsync(treatDirtyAsClean: true);
                await recoverySnapshotStore.PruneExceptAsync(Array.Empty<string>());
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
        }
    }

    private async Task<bool> SaveAllForWindowCloseAsync(
        IEnumerable<DocumentSession> dirtySessions)
    {
        var completed = await WorkspaceTabOperations.RunSequentiallyAsync(
            dirtySessions.ToArray(),
            async session =>
            {
                if (!sessions.Contains(session) || !session.IsDirty)
                {
                    return true;
                }

                DocumentTabs.SelectedItem = session.TabItem;
                return await RequestSaveForWindowCloseAsync(session);
            });

        return completed && sessions.All(session => !session.IsDirty);
    }

    private async Task<bool> RequestSaveForWindowCloseAsync(
        DocumentSession session)
    {
        if (!sessions.Contains(session) ||
            !session.IsDirty ||
            session.CoreWebView is null ||
            session.WindowCloseSaveCompletion is not null ||
            session.CloseCompletion is not null)
        {
            return !session.IsDirty;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.WindowCloseSaveCompletion = completion;
        try
        {
            session.CoreWebView.PostWebMessageAsJson(
                BridgeEventJson.Create(
                    "document.saveRequested",
                    new { reason = "close" }));
            return await completion.Task;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            session.WindowCloseSaveCompletion = null;
            completion.TrySetResult(false);
            return false;
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

        _ = PersistWorkspaceAsync();
    }

    private async Task PersistWorkspaceAsync(bool treatDirtyAsClean = false)
    {
        if (restoringWorkspace)
        {
            return;
        }

        var orderedSessions = GetOrderedSessions();
        var activeSession = ActiveSession ??
            (lastDocumentSession is not null && sessions.Contains(lastDocumentSession)
                ? lastDocumentSession
                : null);
        var state = new WorkspaceState(
            WorkspaceState.CurrentVersion,
            orderedSessions
                .Where(session =>
                    session.DocumentService.DocumentPath is not null ||
                    (!treatDirtyAsClean && session.IsDirty))
                .Select(session => new WorkspaceTabState(
                    session.DocumentService.DocumentPath,
                    !treatDirtyAsClean && session.IsDirty,
                    session.RecoveryId,
                    session.DisplayName,
                    session.RecoveryUpdatedAt))
                .ToArray(),
            activeSession?.DocumentService.DocumentPath,
            recentFiles.ToArray(),
            activeSession?.RecoveryId);

        await workspaceStateGate.WaitAsync();
        try
        {
            await workspaceStateStore.SaveAsync(state);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Workspace persistence failed: {exception}");
        }
        finally
        {
            workspaceStateGate.Release();
        }
    }

    private DocumentSession? FindSession(TabViewItem tab) =>
        sessions.FirstOrDefault(session => ReferenceEquals(session.TabItem, tab));

    private string NextUntitledName()
    {
        untitledSequence++;
        return untitledSequence == 1 ? "Untitled" : $"Untitled {untitledSequence}";
    }

    private void UpdateTabHeader(DocumentSession session)
    {
        session.TabItem.Header = session.HeaderText;
        ToolTipService.SetToolTip(session.TabItem, session.DisplayName);
        var accessibilityStates = new List<string>();
        if (session.DocumentService.DocumentPath is null)
        {
            accessibilityStates.Add("new drawing");
        }
        accessibilityStates.Add(session.IsDirty ? "unsaved changes" : "saved");
        if (session.IsSuspended || session.IsUnloaded)
        {
            accessibilityStates.Add("sleeping");
        }
        if (session.ExternalFileState is not ExternalFileState.None)
        {
            accessibilityStates.Add("changed on disk");
        }

        AutomationProperties.SetName(
            session.TabItem,
            $"{session.DisplayName}, {string.Join(", ", accessibilityStates)}");
        AutomationProperties.SetHelpText(
            session.TabItem,
            session.DocumentService.DocumentPath ?? "Unsaved drawing");
    }

    private void UpdateWindowTitle()
    {
        var active = ActiveSession;
        UpdateFileMenuState(active);
        if (settingsPageVisible)
        {
            Title = "Settings — Excalidraw Desktop";
            return;
        }
        UpdateStatusBar(active);
        if (active is null || !active.IsReady)
        {
            Title = "Excalidraw Desktop — Starting…";
            return;
        }

        var dirtyMarker = active.IsDirty ? " ●" : string.Empty;
        Title = active.DisplayName == "Untitled"
            ? $"Excalidraw Desktop{dirtyMarker}"
            : $"{active.DisplayName}{dirtyMarker} — Excalidraw Desktop";
        if (active.ExternalFileState is not ExternalFileState.None)
        {
            Title += " — Changed on disk";
        }
    }

    private void UpdateFileMenuState(DocumentSession? active)
    {
        var canUseEditor = !settingsPageVisible && active is
        {
            IsReady: true,
            IsResuming: false,
            IsRestoringFromHibernation: false,
            CoreWebView: not null,
        };
        SaveMenuItem.IsEnabled = canUseEditor;
        SaveAsMenuItem.IsEnabled = canUseEditor;
        SaveAllMenuItem.IsEnabled = !settingsPageVisible && sessions.Any(session =>
            session.IsDirty &&
            session.IsReady &&
            !session.IsResuming &&
            session.CoreWebView is not null);
        CloseTabMenuItem.IsEnabled = settingsPageVisible || active is not null;
    }

    private void UpdateStatusBar(DocumentSession? session)
    {
        if (session is null)
        {
            return;
        }

        var path = session.DocumentService.DocumentPath;
        string status;
        var actionable = false;
        if (session.IsResuming)
        {
            status = "Resuming";
        }
        else if (session.IsUnloaded || session.IsUnloading ||
            session.IsSuspended || session.IsSuspensionChanging)
        {
            status = "Sleeping";
        }
        else if (session.ExternalFileState == ExternalFileState.Modified)
        {
            status = "Changed on disk — Resolve";
            actionable = true;
        }
        else if (session.ExternalFileState == ExternalFileState.Deleted)
        {
            status = "File missing — Resolve";
            actionable = true;
        }
        else if (session.ExternalFileState == ExternalFileState.Moved)
        {
            status = "File moved — Resolve";
            actionable = true;
        }
        else if (session.IsDirty)
        {
            status = session.RecoveryUpdatedAt is { } recoveredAt
                ? $"Unsaved • recovered {recoveredAt.ToLocalTime():t}"
                : "Unsaved changes";
        }
        else
        {
            status = path is null ? "New drawing" : "Saved";
        }

        session.CoreWebView?.PostWebMessageAsJson(
            BridgeEventJson.Create(
                "document.statusChanged",
                new
                {
                    document = path ?? session.DisplayName,
                    status,
                    actionable,
                }));
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

        await Launcher.LaunchUriAsync(uri);
    }

}
