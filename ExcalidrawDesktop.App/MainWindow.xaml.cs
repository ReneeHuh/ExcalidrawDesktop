using System.Diagnostics;
using System.Runtime.InteropServices;
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
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.StartScreen;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow : Window
{
    private const int InvalidStateHResult = unchecked((int)0x8007139F);
    private const string WebView2HelpUri =
        "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";
    private static readonly TimeSpan ExitActivationTimeout =
        TimeSpan.FromSeconds(3);

    private readonly string webAssetPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Web");
    private readonly ApplicationWorkspaceCoordinator workspaceCoordinator;
    private readonly bool restoreWorkspace;
    private readonly List<DocumentSession> sessions = [];
    private readonly List<string> recentFiles;
    private readonly Queue<string> pendingActivatedFiles = [];
    private readonly DispatcherTimer suspensionTimer = new();
    private readonly ImageExportService imageExportService = new();
    private readonly RecoverySnapshotStore recoverySnapshotStore;
    private readonly bool runTabSmoke;
    private readonly bool runRecoverySmoke;
    private readonly bool verifyRecoverySmoke;
    private readonly bool runTitleBarSmoke;
    private readonly bool runMultiWindowSmoke;
    private readonly bool runMultiWindowExitSmoke;
    private readonly bool runMultiWindowDirtyExitSmoke;
    private readonly bool runSuspensionSmoke;
    private readonly bool runImageExportSmoke;
    private readonly bool runDocumentSafetySmoke;
    private readonly bool runCloseDecisionsSmoke;
    private readonly bool runLocalizationSmoke;
    private readonly string? localizationSmokeLanguage;
    private readonly bool performanceSuspendInactive;
    private readonly bool performanceUnloadInactive;
    private readonly int performanceTabCount;
    private readonly Stopwatch performanceStartup = Stopwatch.StartNew();
    private DesktopPreferences desktopPreferences = DesktopPreferences.Default;
    private DocumentSession? lastDocumentSession;
    private TabViewItem? settingsTabItem;
    private bool allowClose;
    private bool isWindowReady;
    private bool isWindowActive;
    private bool tabSmokeStarted;
    private bool titleBarRegistered;
    private bool titleBarRootSubscribed;
    private bool titleBarSmokeStarted;
    private bool multiWindowSmokeStarted;
    private bool multiWindowExitSmokeStarted;
    private MainWindow? pendingTearOutWindow;
    private bool performanceSmokeStarted;
    private bool suspensionSmokeStarted;
    private bool imageExportSmokeStarted;
    private bool documentSafetySmokeStarted;
    private bool closeDecisionsSmokeStarted;
    private readonly HashSet<DocumentSession> localizationSmokeEditors = [];
    private DocumentSession? localizationSmokeRetryTarget;
    private bool localizationSmokeRetryStarted;
    private bool localizationSmokeComplete;
    private string? imageExportSmokeOutputPath;
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
#if DEBUG
    private TaskCompletionSource? multiWindowInitializationRelease;
#endif

    internal MainWindow(
        ApplicationWorkspaceCoordinator workspaceCoordinator,
        bool runTabSmoke = false,
        string? workspaceStatePath = null,
        bool runRecoverySmoke = false,
        bool verifyRecoverySmoke = false,
        bool runTitleBarSmoke = false,
        int performanceTabCount = 0,
        bool runSuspensionSmoke = false,
        bool performanceSuspendInactive = false,
        bool performanceUnloadInactive = false,
        bool restoreWorkspace = true,
        bool createInitialTab = true,
        bool runMultiWindowSmoke = false,
        bool runMultiWindowExitSmoke = false,
        bool runMultiWindowDirtyExitSmoke = false,
        bool runImageExportSmoke = false,
        bool runDocumentSafetySmoke = false,
        bool runCloseDecisionsSmoke = false,
        bool runLocalizationSmoke = false,
        string? localizationSmokeLanguage = null)
    {
        InitializeComponent();
        Title = DesktopResources.Get(
            "StartingWindowTitle",
            "Excalidraw Desktop — Starting…");
        this.workspaceCoordinator = workspaceCoordinator;
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
        MainLayout.ActualThemeChanged += OnActualThemeChanged;
        ApplyDesktopTheme();
        ToolTipService.SetToolTip(
            SettingsButton,
            DesktopResources.Get("SettingsButtonToolTip", "Show Settings tab"));
        InitializeTitleBar();
        var effectiveWorkspaceStatePath = workspaceStatePath ?? Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path,
                "workspace-state.json");
        recoverySnapshotStore = new RecoverySnapshotStore(Path.Combine(
            Path.GetDirectoryName(effectiveWorkspaceStatePath)!,
            $"{Path.GetFileNameWithoutExtension(effectiveWorkspaceStatePath)}.recovery"));
        this.runTabSmoke = runTabSmoke;
        this.runRecoverySmoke = runRecoverySmoke;
        this.verifyRecoverySmoke = verifyRecoverySmoke;
        this.runTitleBarSmoke = runTitleBarSmoke;
        this.runMultiWindowSmoke = runMultiWindowSmoke;
        this.runMultiWindowExitSmoke = runMultiWindowExitSmoke;
        this.runMultiWindowDirtyExitSmoke = runMultiWindowDirtyExitSmoke;
        this.runImageExportSmoke = runImageExportSmoke;
        this.runDocumentSafetySmoke = runDocumentSafetySmoke;
        this.runCloseDecisionsSmoke = runCloseDecisionsSmoke;
        this.runLocalizationSmoke = runLocalizationSmoke;
        this.localizationSmokeLanguage = localizationSmokeLanguage;
        this.performanceTabCount = performanceTabCount;
        this.runSuspensionSmoke = runSuspensionSmoke;
        this.performanceSuspendInactive = performanceSuspendInactive;
        this.performanceUnloadInactive = performanceUnloadInactive;
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
        if (runMultiWindowSmoke)
        {
            CreateTab();
        }
        if (runSuspensionSmoke)
        {
            CreateTab();
            CreateTab();
        }
        if (runDocumentSafetySmoke)
        {
            CreateTab();
        }
        if (runCloseDecisionsSmoke)
        {
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
        workspaceCoordinator.UpdatePreferences(args.Preferences);
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

    internal void ApplySharedPreferences(DesktopPreferences preferences)
    {
        desktopPreferences = preferences;
        AppSettingsPage.LoadPreferences(
            desktopPreferences,
            workspaceCoordinator.EffectiveLanguage,
            workspaceCoordinator.StartupLanguagePreference);
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
        if (!CanPostEditorMessage(session))
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
        if (!CanPostEditorMessage(session))
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

    private static bool CanPostEditorMessage(DocumentSession session) =>
        session.IsReady &&
        !session.IsSuspended &&
        !session.IsSuspensionChanging &&
        !session.IsUnloading &&
        !session.IsUnloaded &&
        !session.IsResuming &&
        session.CoreWebView is not null;

    private static void TryPostEditorMessage(
        DocumentSession session,
        string message)
    {
        try
        {
            session.CoreWebView!.PostWebMessageAsJson(message);
        }
        catch (COMException exception) when (
            exception.HResult == InvalidStateHResult)
        {
            Debug.WriteLine(
                $"Editor message skipped during a WebView lifecycle transition: {exception.Message}");
        }
    }

    private void OnEditorLanguageApplied(
        DocumentSession session,
        string langCode,
        string direction)
    {
#if DEBUG
        if (!runLocalizationSmoke ||
            localizationSmokeComplete ||
            !session.IsReady ||
            localizationSmokeLanguage is null)
        {
            return;
        }

        var expected = workspaceCoordinator.EffectiveLanguage;
        var expectedFlowDirection = expected.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        var nativeResourcesApplied = string.Equals(
                SettingsMenuItem.Text,
                DesktopResources.Get("SettingsTabTitle", "Settings"),
                StringComparison.Ordinal) &&
            string.Equals(
                AutomationProperties.GetName(SettingsButton),
                DesktopResources.Get(
                    "SettingsButtonToolTip",
                    "Show Settings tab"),
                StringComparison.Ordinal) &&
            !string.Equals(FileMenu.Title, "File", StringComparison.Ordinal);
        var valid = string.Equals(
                localizationSmokeLanguage,
                expected.PreferenceTag,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                langCode,
                expected.ExcalidrawCode,
                StringComparison.Ordinal) &&
            string.Equals(direction, expected.Direction, StringComparison.Ordinal) &&
            MainLayout.FlowDirection == expectedFlowDirection &&
            nativeResourcesApplied;
        if (!valid)
        {
            CompleteLocalizationSmoke(
                passed: false,
                langCode,
                direction,
                nativeResourcesApplied,
                "The native or web locale did not match the requested language.");
            return;
        }

        if (localizationSmokeRetryStarted &&
            ReferenceEquals(localizationSmokeRetryTarget, session))
        {
            CompleteLocalizationSmoke(
                passed: true,
                langCode,
                direction,
                nativeResourcesApplied,
                failure: null);
            return;
        }

        if (!localizationSmokeEditors.Add(session))
        {
            return;
        }

        if (localizationSmokeEditors.Count == 1)
        {
            CreateTab();
            return;
        }

        localizationSmokeRetryTarget = session;
        localizationSmokeRetryStarted = true;
        _ = RetrySessionAsync(session);
#endif
    }

#if DEBUG
    private void CompleteLocalizationSmoke(
        bool passed,
        string langCode,
        string direction,
        bool nativeResourcesApplied,
        string? failure)
    {
        localizationSmokeComplete = true;
        var resultPath = Path.Combine(
            AppContext.BaseDirectory,
            "localization-smoke-result.json");
        File.WriteAllText(
            resultPath,
            JsonSerializer.Serialize(new
            {
                Result = passed ? "Passed" : "Failed",
                RequestedLanguage = localizationSmokeLanguage,
                NativeLanguage = workspaceCoordinator.EffectiveLanguage.WinUiTag,
                ExcalidrawLanguage = langCode,
                Direction = direction,
                NativeResourcesApplied = nativeResourcesApplied,
                FileMenuTitle = FileMenu.Title,
                SettingsMenuText = SettingsMenuItem.Text,
                DynamicSettingsText = DesktopResources.Get(
                    "SettingsTabTitle",
                    "Settings"),
                SettingsButtonName = AutomationProperties.GetName(SettingsButton),
                ExpectedSettingsButtonName = DesktopResources.Get(
                    "SettingsButtonToolTip",
                    "Show Settings tab"),
                NativeFlowDirection = MainLayout.FlowDirection.ToString(),
                NewEditorValidated = localizationSmokeEditors.Count >= 2,
                RetriedEditorValidated = localizationSmokeRetryStarted,
                Failure = failure,
            }));
        Title = passed
            ? $"Excalidraw Desktop — Localization smoke passed: {localizationSmokeLanguage}"
            : $"Excalidraw Desktop — Localization smoke failed: {localizationSmokeLanguage}";
    }
#endif

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
            _ = InitializeSessionAsync(session);
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
            await ShowOpenErrorAsync(DesktopResources.Get(
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
        var existing = workspaceCoordinator.FindSessionByPath(canonicalPath);
        if (existing is not null)
        {
            workspaceCoordinator.ActivateSession(
                existing.Value.Window,
                existing.Value.Session);
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
            await ShowOpenErrorAsync(GetLocalizedDocumentError(
                exception,
                DesktopResources.Get(
                    "ActivatedDrawingOpenFailed",
                    "The activated drawing could not be opened.")));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await ShowOpenErrorAsync(DesktopResources.Get(
                "ActivatedDrawingOpenFailed",
                "The activated drawing could not be opened."));
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
        session.Dispatcher = new BridgeDispatcher(
            session.DocumentService,
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
            () => QueueCloseSession(session),
            next => SelectAdjacentTab(session, next),
            (exportId, message) =>
                _ = FailImageExportAsync(session, exportId, message),
            (langCode, direction) =>
                OnEditorLanguageApplied(session, langCode, direction));
        EventHandler retryRequested = (_, _) => _ = RetrySessionAsync(session);
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
        };
        ConfigureEditorDropTarget(session, session.Content.Editor);
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
                _ = WakeUnloadedSessionAsync(session);
            }
            else
            {
                _ = InitializeSessionAsync(session);
            }
            _ = CheckExternalFileStateAsync(session, showPrompt: true);
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
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        DocumentTabs.TabItemsChanged -= OnTabItemsChanged;
        pendingTearOutWindow?.CloseIfEmptyAfterMove();
        pendingTearOutWindow = null;
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
        !session.IsExporting &&
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
        !session.IsExporting &&
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
        !session.IsExporting &&
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
        var editor = session.Content.RecreateEditor(DesktopResources.Get(
            "ResumingDrawing",
            "Resuming drawing…"));
        ConfigureEditorDropTarget(session, editor);

        if (session.HibernatedContent is { } content)
        {
            if (!session.DocumentService.StageActiveFileReload())
            {
                session.IsRestoringFromHibernation = false;
                session.IsResuming = false;
                session.Content.ShowFailure(DesktopResources.Get(
                    "DrawingResumeFailed",
                    "This drawing could not be resumed."));
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
        !session.IsExporting &&
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
        SendEditorTheme(session);
        SendEditorLanguage(session);
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
#if DEBUG
        if (runMultiWindowSmoke && multiWindowInitializationRelease is { } release)
        {
            await release.Task;
        }
#endif
        var webView = session.Content.Editor;
        Title = DesktopResources.Get(
            "InitializingEditorTitle",
            "Excalidraw Desktop — Initializing editor…");
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
            var entryPoint = runTabSmoke || runMultiWindowSmoke || runSuspensionSmoke ||
                runRecoverySmoke || verifyRecoverySmoke ||
                runDocumentSafetySmoke || runCloseDecisionsSmoke
                    ? $"{session.TabOrigin.EntryPoint}?desktopSmoke=1"
                    : session.TabOrigin.EntryPoint;
            webView.CoreWebView2.Navigate(entryPoint);
        }
        catch (Exception exception)
        {
            var runtimeMissing = IsWebView2RuntimeUnavailable(exception);
            session.LastLifecycleFailure = exception.Message;
            session.Content.ShowFailure(
                runtimeMissing
                    ? DesktopResources.Get(
                        "WebView2Required",
                        "Microsoft Edge WebView2 Runtime is required.")
                    : DesktopResources.Get(
                        "DrawingStartFailed",
                        "This drawing could not start."),
                runtimeMissing
                    ? DesktopResources.Get(
                        "WebView2RepairGuidance",
                        "Install or repair the Evergreen WebView2 Runtime, then choose Retry. Your saved drawing is not changed.")
                    : DesktopResources.Get(
                        "EditorRetryGuidance",
                        "Retry the editor. If the problem continues, repair WebView2 or rebuild the desktop app. Your saved drawing is not changed."),
                showWebView2Help: true);
            Title = DesktopResources.Get(
                "EditorStartupFailedTitle",
                "Excalidraw Desktop — Editor startup failed");
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
        var exportFilter =
            $"{ImageExportPolicy.GetUploadOrigin(session.TabOrigin)}/_desktop/export/*";
        coreWebView.AddWebResourceRequestedFilter(
            exportFilter,
            CoreWebView2WebResourceContext.All);

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
        TypedEventHandler<CoreWebView2, CoreWebView2WebResourceRequestedEventArgs> webResourceRequested =
            (_, args) => OnImageExportWebResourceRequested(session, coreWebView, args);

        coreWebView.NavigationStarting += navigationStarting;
        coreWebView.NavigationCompleted += navigationCompleted;
        coreWebView.NewWindowRequested += newWindowRequested;
        coreWebView.PermissionRequested += permissionRequested;
        coreWebView.WebMessageReceived += webMessageReceived;
        coreWebView.ProcessFailed += processFailed;
        coreWebView.WebResourceRequested += webResourceRequested;
        session.DetachWebViewHandlers = () =>
        {
            coreWebView.NavigationStarting -= navigationStarting;
            coreWebView.NavigationCompleted -= navigationCompleted;
            coreWebView.NewWindowRequested -= newWindowRequested;
            coreWebView.PermissionRequested -= permissionRequested;
            coreWebView.WebMessageReceived -= webMessageReceived;
            coreWebView.ProcessFailed -= processFailed;
            coreWebView.WebResourceRequested -= webResourceRequested;
            coreWebView.RemoveWebResourceRequestedFilter(
                exportFilter,
                CoreWebView2WebResourceContext.All);
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
        var editor = session.Content.RecreateEditor(DesktopResources.Get(
            "RetryingEditor",
            "Retrying editor…"));
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
                DesktopResources.Format(
                    "DrawingLoadFailedFormat",
                    "This drawing could not load ({0}).",
                    args.WebErrorStatus),
                DesktopResources.Get(
                    "DrawingLoadFailureGuidance",
                    "Check the local app installation and retry. If other WebView2 apps also fail, use WebView2 help to repair the runtime."),
                showWebView2Help: true);
            Title = DesktopResources.Get(
                "EditorStartupFailedTitle",
                "Excalidraw Desktop — Editor startup failed");
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
            DesktopResources.Get(
                "EditorBridgeNotReady",
                "The editor loaded but its desktop bridge did not become ready."),
            DesktopResources.Get(
                "EditorBridgeRetryGuidance",
                "Retry the editor. If the problem continues, rebuild the local web assets and desktop app."));
#endif
        session.LastLifecycleFailure = "The desktop bridge did not become ready.";
        Title = DesktopResources.Get(
            "EditorStartupFailedTitle",
            "Excalidraw Desktop — Editor startup failed");
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

        HandleWebViewProcessFailure(session, args.ProcessFailedKind.ToString());
    }

    private void HandleWebViewProcessFailure(
        DocumentSession session,
        string failureKind)
    {
        session.IsReady = false;
        session.IsSuspended = false;
        session.LastLifecycleFailure = failureKind;
        if (session.PendingImageExport is { } pendingExport)
        {
            _ = FailImageExportAsync(
                session,
                pendingExport.ExportId,
                DesktopResources.Get(
                    "EditorStoppedDuringExport",
                    "The editor stopped while creating the PNG."));
        }
        session.Content.ShowFailure(
            DesktopResources.Get(
                "EditorProcessStopped",
                "The editor process stopped unexpectedly."),
            DesktopResources.Format(
                "EditorProcessFailureGuidanceFormat",
                "WebView2 reported {0}. Retry the editor. If failures continue, repair the WebView2 Runtime.",
                failureKind),
            showWebView2Help: true);
        UpdateTabHeader(session);
        UpdateStatusBar(session);
        Title = DesktopResources.Get(
            "EditorProcessFailedTitle",
            "Excalidraw Desktop — Editor process failed");
    }

#if DEBUG
    private void SimulateWebViewProcessFailureForSmoke(DocumentSession session) =>
        HandleWebViewProcessFailure(session, "SmokeProcessFailure");
#endif

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
            session.BridgeDispatchDepth++;
            var message = BridgeMessageParser.Parse(args.WebMessageAsJson);
            await session.Dispatcher.DispatchAsync(coreWebView, message);
        }
        catch (BridgeProtocolException exception)
        {
            Debug.WriteLine($"Rejected bridge message ({exception.Code}): {exception.Message}");
        }
        finally
        {
            session.BridgeDispatchDepth--;
        }
    }

    private async void OnImageExportWebResourceRequested(
        DocumentSession session,
        CoreWebView2 coreWebView,
        CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (session.PendingImageExport is not { } pending ||
            !sessions.Contains(session) ||
            !ReferenceEquals(session.CoreWebView, coreWebView) ||
            !ImageExportPolicy.IsMatchingUpload(
                args.Request.Uri,
                session.TabOrigin,
                pending.ExportId))
        {
            return;
        }

        string? ReadHeader(string name)
        {
            try
            {
                return args.Request.Headers.GetHeader(name);
            }
            catch
            {
                return null;
            }
        }

        var requestExportId = ReadHeader("X-Excalidraw-Export-Id");
        var contentType = ReadHeader("Content-Type");
        var requestOrigin = ReadHeader("Origin");
        var requestedMethod = ReadHeader("Access-Control-Request-Method");
        var requestedHeaders = ReadHeader("Access-Control-Request-Headers");

        if (!string.Equals(
                requestOrigin,
                session.TabOrigin.Origin,
                StringComparison.OrdinalIgnoreCase))
        {
            args.Response = CreateImageExportResponse(
                coreWebView,
                403,
                "Forbidden",
                session.TabOrigin.Origin);
            return;
        }

        if (string.Equals(
                args.Request.Method,
                "OPTIONS",
                StringComparison.OrdinalIgnoreCase))
        {
            var validPreflight = string.Equals(
                    requestedMethod,
                    "POST",
                    StringComparison.OrdinalIgnoreCase) &&
                requestedHeaders?.Contains(
                    "x-excalidraw-export-id",
                    StringComparison.OrdinalIgnoreCase) == true;
            args.Response = CreateImageExportResponse(
                coreWebView,
                validPreflight ? 204 : 400,
                validPreflight ? "No Content" : "Bad Request",
                session.TabOrigin.Origin,
                includePreflightHeaders: validPreflight);
            return;
        }

        if (!string.Equals(
                args.Request.Method,
                "POST",
                StringComparison.OrdinalIgnoreCase) ||
            args.Request.Content is null ||
            !string.Equals(
                requestExportId,
                pending.ExportId.ToString("D"),
                StringComparison.OrdinalIgnoreCase) ||
            contentType?.StartsWith("image/png", StringComparison.OrdinalIgnoreCase) != true)
        {
            args.Response = CreateImageExportResponse(
                coreWebView,
                400,
                "Bad Request",
                session.TabOrigin.Origin);
            _ = FailImageExportAsync(
                session,
                pending.ExportId,
                DesktopResources.Get(
                    "InvalidPngExportData",
                    "The editor sent invalid PNG export data."));
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            await ImageExportService.WritePngAsync(
                pending.Destination,
                args.Request.Content,
                pending.Cancellation.Token);
            if (session.PendingImageExport?.ExportId != pending.ExportId)
            {
                throw new OperationCanceledException();
            }

            CompleteImageExport(session, pending, pending.Destination.Name);
            args.Response = CreateImageExportResponse(
                coreWebView,
                204,
                "No Content",
                session.TabOrigin.Origin);
        }
        catch (OperationCanceledException)
        {
            args.Response = CreateImageExportResponse(
                coreWebView,
                409,
                "Cancelled",
                session.TabOrigin.Origin);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"PNG export failed: {exception}");
            await FailImageExportAsync(
                session,
                pending.ExportId,
                GetImageExportFailureMessage(exception));
            args.Response = CreateImageExportResponse(
                coreWebView,
                500,
                "Export Failed",
                session.TabOrigin.Origin);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static CoreWebView2WebResourceResponse CreateImageExportResponse(
        CoreWebView2 coreWebView,
        int statusCode,
        string reasonPhrase,
        string allowedOrigin,
        bool includePreflightHeaders = false)
    {
        var headers = $"Access-Control-Allow-Origin: {allowedOrigin}\r\n" +
            "Vary: Origin\r\nContent-Type: text/plain";
        if (includePreflightHeaders)
        {
            headers += "\r\nAccess-Control-Allow-Methods: POST" +
                "\r\nAccess-Control-Allow-Headers: Content-Type, X-Excalidraw-Export-Id" +
                "\r\nAccess-Control-Max-Age: 600";
        }
        return coreWebView.Environment.CreateWebResourceResponse(
            new InMemoryRandomAccessStream(),
            statusCode,
            reasonPhrase,
            headers);
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
        SendEditorLanguage(session);
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

            var existing = workspaceCoordinator.FindSessionByPath(
                document.CanonicalPath);
            if (existing is not null)
            {
                workspaceCoordinator.ActivateSession(
                    existing.Value.Window,
                    existing.Value.Session);
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
            await ShowOpenErrorAsync(GetLocalizedDocumentError(
                exception,
                DesktopResources.Get(
                    "SelectedDrawingOpenFailed",
                    "The selected drawing could not be opened.")));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await ShowOpenErrorAsync(DesktopResources.Get(
                "SelectedDrawingOpenFailed",
                "The selected drawing could not be opened."));
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
            Title = DesktopResources.Get(
                "OpenDrawingErrorTitle",
                "Could not open drawing"),
            Content = message,
            CloseButtonText = DesktopResources.Get("OkButton", "OK"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private static string GetLocalizedDocumentError(
        BridgeProtocolException exception,
        string fallback) => exception.Code switch
        {
            "DocumentNotFound" => DesktopResources.Get(
                "DocumentNotFoundMessage",
                "The drawing no longer exists at that location."),
            "DocumentAccessDenied" => DesktopResources.Get(
                "DocumentAccessDeniedMessage",
                "Excalidraw Desktop does not have permission to access that drawing."),
            "DocumentReadFailed" => DesktopResources.Get(
                "DocumentReadFailedMessage",
                "The drawing could not be read."),
            "DocumentTooLarge" => DesktopResources.Get(
                "DocumentTooLargeMessage",
                "The selected drawing exceeds the 50 MB desktop document limit."),
            "DocumentPathUnavailable" => DesktopResources.Get(
                "DocumentPathUnavailableMessage",
                "The selected drawing does not have a local file path."),
            "DocumentChangedExternally" => DesktopResources.Get(
                "DocumentChangedExternallyMessage",
                "The drawing changed outside Excalidraw Desktop. Resolve the conflict before saving."),
            "DocumentAlreadyOpen" => DesktopResources.Get(
                "DocumentAlreadyOpenMessage",
                "That drawing is already open in another tab. Choose a different file name."),
            "DocumentWriteFailed" => DesktopResources.Get(
                "DocumentWriteFailedMessage",
                "The drawing could not be written to disk."),
            "DocumentInvalid" => DesktopResources.Get(
                "DocumentInvalidMessage",
                "The selected file is not a valid Excalidraw drawing."),
            _ => fallback,
        };

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

                        var document = await target.DocumentService.OpenPathAsync(path);
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
            var existing = workspaceCoordinator.FindSessionByPath(canonicalPath);
            if (existing is not null)
            {
                workspaceCoordinator.ActivateSession(
                    existing.Value.Window,
                    existing.Value.Session);
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
            await ShowOpenErrorAsync(GetLocalizedDocumentError(
                exception,
                DesktopResources.Get(
                    "RecentDrawingOpenFailed",
                    "The recent drawing could not be opened.")));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await ShowOpenErrorAsync(DesktopResources.Get(
                "RecentDrawingOpenFailed",
                "The recent drawing could not be opened."));
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
                    ? DesktopResources.Get(
                        "DrawingFileMissingTitle",
                        "Drawing file is missing")
                    : DesktopResources.Get(
                        "DrawingChangedOutsideTitle",
                        "Drawing changed outside the app"),
                Content = requiresLocate
                    ? DesktopResources.Get(
                        "DrawingFileMissingContent",
                        "The backing file was moved or deleted. Locate it, save this tab to a new file, or keep editing without overwriting anything.")
                    : DesktopResources.Get(
                        "DrawingChangedOutsideContent",
                        "Reload the disk version, save this tab to a different file, or keep editing. The existing file will not be overwritten automatically."),
                PrimaryButtonText = requiresLocate
                    ? DesktopResources.Get("LocateFileButton", "Locate file")
                    : DesktopResources.Get("ReloadButton", "Reload"),
                SecondaryButtonText = DesktopResources.Get("SaveAsButton", "Save As"),
                CloseButtonText = DesktopResources.Get("KeepEditingButton", "Keep editing"),
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
            await ShowOpenErrorAsync(DesktopResources.Get(
                "FileConflictResolutionFailed",
                "The file conflict could not be resolved."));
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

        var existing = workspaceCoordinator.FindSessionByPath(
            document.CanonicalPath);
        if (existing is not null &&
            !ReferenceEquals(existing.Value.Session, session))
        {
            workspaceCoordinator.ActivateSession(
                existing.Value.Window,
                existing.Value.Session);
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
            item.Click += (_, _) => _ = OpenRecentFileAsync(path);
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
    private void TryRunMultiWindowSmoke()
    {
        if (!runMultiWindowSmoke || multiWindowSmokeStarted)
        {
            return;
        }

        if (sessions.FirstOrDefault(session => !session.IsReady) is { } pending)
        {
            DocumentTabs.SelectedItem = pending.TabItem;
            return;
        }

        multiWindowSmokeStarted = true;
        _ = RunMultiWindowSmokeAsync();
    }

    private void TryRunMultiWindowExitSmoke()
    {
        if ((!runMultiWindowExitSmoke && !runMultiWindowDirtyExitSmoke) ||
            multiWindowExitSmokeStarted ||
            !isWindowReady ||
            restoringWorkspace ||
            sessions.Any(session => !session.IsReady))
        {
            return;
        }

        multiWindowExitSmokeStarted = true;
        _ = RunMultiWindowExitSmokeAsync();
    }

    private async Task RunMultiWindowExitSmokeAsync()
    {
        try
        {
            var destination = workspaceCoordinator.CreateWindow();
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
            while ((!destination.IsReadyForActivation ||
                    destination.OpenSessions.Count != 1 ||
                    !destination.OpenSessions[0].IsReady) &&
                DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            if (!destination.IsReadyForActivation ||
                destination.OpenSessions.Count != 1 ||
                !destination.OpenSessions[0].IsReady ||
                workspaceCoordinator.Windows.Count != 2)
            {
                throw new InvalidOperationException(
                    "The second clean window did not become ready for Exit.");
            }

            if (runMultiWindowDirtyExitSmoke)
            {
                await Task.Delay(250);
                var dirtyMessage = BridgeEventJson.Create(
                    "document.dirtyChanged",
                    new { isDirty = true });
                foreach (var targetWindow in workspaceCoordinator.Windows)
                {
                    var targetSession = targetWindow.OpenSessions[0];
                    targetSession.ForceDirtyForSmoke = true;
                    await targetSession.CoreWebView!.ExecuteScriptAsync(
                        $"window.chrome.webview.postMessage({dirtyMessage})");
                }
                var dirtyDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
                while (workspaceCoordinator.Windows.Any(targetWindow =>
                        !targetWindow.OpenSessions[0].IsDirty) &&
                    DateTimeOffset.UtcNow < dirtyDeadline)
                {
                    await Task.Delay(50);
                }
                if (workspaceCoordinator.Windows.Any(targetWindow =>
                    !targetWindow.OpenSessions[0].IsDirty))
                {
                    throw new InvalidOperationException(
                        "The dirty Exit fixture did not reach both native sessions.");
                }
            }

            await workspaceCoordinator.PersistWorkspaceAsync();
            await File.WriteAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "multi-window-exit-smoke.ready"),
                "ready");
            await workspaceCoordinator.RequestExitAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            if (!resourcesDisposed)
            {
                Title = $"Excalidraw Desktop — Multi-window Exit smoke failed: {exception.Message}";
            }
        }
    }

    private async Task RunMultiWindowSmokeAsync()
    {
        var paths = new List<string>();
        try
        {
            if (sessions.Count != 2 || sessions.Any(session =>
                session.CoreWebView is null || !session.IsReady))
            {
                throw new InvalidOperationException(
                    "The multi-window smoke test requires two ready drawing tabs.");
            }

            var sourceSession = sessions[0];
            var session = sessions[1];
            var originalOrigin = session.TabOrigin;
            var originalRecoveryId = session.RecoveryId;
            var originalWindowCount = workspaceCoordinator.Windows.Count;

            session.InactiveSince = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(6);
            if (!await SuspendSessionAsync(session, requireIdle: true) ||
                !session.IsSuspended)
            {
                throw new InvalidOperationException(
                    "The transfer fixture could not suspend its inactive drawing.");
            }
            if (workspaceCoordinator.MoveSessionToNewWindow(this, session) ||
                !sessions.Contains(session) ||
                workspaceCoordinator.Windows.Count != originalWindowCount)
            {
                throw new InvalidOperationException(
                    "A suspended drawing was allowed to enter a transfer.");
            }

            ResumeSession(session);
            if (!session.IsResuming || session.IsSuspended ||
                workspaceCoordinator.MoveSessionToNewWindow(this, session) ||
                !sessions.Contains(session))
            {
                throw new InvalidOperationException(
                    "A resuming drawing was allowed to enter a transfer.");
            }
            if (!await WaitUntilAsync(
                    () => !session.IsResuming,
                    TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException(
                    "The transfer fixture did not finish resuming.");
            }

            session.InactiveSince = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(16);
            if (!await UnloadSessionAsync(session, requireIdle: true) ||
                !session.IsUnloaded ||
                workspaceCoordinator.MoveSessionToNewWindow(this, session) ||
                !sessions.Contains(session))
            {
                throw new InvalidOperationException(
                    "An unloaded drawing was allowed to enter a transfer.");
            }

            var wakeTask = WakeUnloadedSessionAsync(session);
            if (!session.IsRestoringFromHibernation ||
                !session.IsResuming ||
                workspaceCoordinator.MoveSessionToNewWindow(this, session) ||
                !sessions.Contains(session))
            {
                throw new InvalidOperationException(
                    "A recreating drawing was allowed to enter a transfer.");
            }
            await wakeTask;
            if (!await WaitUntilAsync(
                    () => session.IsReady &&
                        !session.IsRestoringFromHibernation &&
                        !session.IsResuming,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The unloaded transfer fixture did not recreate its editor.");
            }

            var coreBeforeFailure = session.CoreWebView;
            SimulateWebViewProcessFailureForSmoke(session);
            if (session.IsReady)
            {
                throw new InvalidOperationException(
                    "The simulated WebView failure left the session ready.");
            }
            if (session.LastLifecycleFailure != "SmokeProcessFailure")
            {
                throw new InvalidOperationException(
                    $"The simulated WebView failure reason changed to '{session.LastLifecycleFailure}'.");
            }
            if (workspaceCoordinator.MoveSessionToNewWindow(this, session) ||
                !sessions.Contains(session))
            {
                throw new InvalidOperationException(
                    "A process-failed drawing entered a transfer.");
            }

            multiWindowInitializationRelease = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var retryTask = RetrySessionAsync(session);
            if (!await WaitUntilAsync(
                    () => session.IsInitializing,
                    TimeSpan.FromSeconds(5)) ||
                workspaceCoordinator.MoveSessionToNewWindow(this, session) ||
                !sessions.Contains(session))
            {
                throw new InvalidOperationException(
                    "An initializing drawing was allowed to enter a transfer.");
            }
            multiWindowInitializationRelease.TrySetResult();
            multiWindowInitializationRelease = null;
            await retryTask;
            if (!await WaitUntilAsync(
                    () => session.IsReady && !session.IsInitializing,
                    TimeSpan.FromSeconds(20)) ||
                session.CoreWebView is null ||
                ReferenceEquals(session.CoreWebView, coreBeforeFailure) ||
                session.TabOrigin != originalOrigin)
            {
                throw new InvalidOperationException(
                    "The process-failed drawing did not recover with its origin intact.");
            }

            var originalCoreWebView = session.CoreWebView;

            var sourcePath = Path.Combine(
                AppContext.BaseDirectory,
                "multi-window-source.excalidraw");
            var movedPath = Path.Combine(
                AppContext.BaseDirectory,
                "multi-window-moved.excalidraw");
            var saveAsPath = Path.Combine(
                AppContext.BaseDirectory,
                "multi-window-moved-save-as.excalidraw");
            var renamedPath = Path.Combine(
                AppContext.BaseDirectory,
                "multi-window-moved-renamed.excalidraw");
            paths.AddRange([sourcePath, movedPath, saveAsPath, renamedPath]);
            var sourceInitial = CreateDocumentSafetyScene(
                "multi-window-source-initial",
                -200);
            var movedInitial = CreateDocumentSafetyScene(
                "multi-window-moved-initial",
                200);
            await File.WriteAllTextAsync(sourcePath, sourceInitial);
            await File.WriteAllTextAsync(movedPath, movedInitial);
            await File.WriteAllTextAsync(
                saveAsPath,
                CreateDocumentSafetyScene("multi-window-save-as-placeholder", 0));
            AttachDocumentToSession(
                sourceSession,
                await sourceSession.DocumentService.OpenPathAsync(sourcePath),
                select: true);
            AttachDocumentToSession(
                session,
                await session.DocumentService.OpenPathAsync(movedPath),
                select: false);
            if (!await WaitUntilAsync(
                    () => sourceSession.PendingDocumentLoad is null &&
                        session.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The multi-window file fixtures did not finish loading.");
            }
            var sourceWatcher = session.ExternalFileWatcher;
            DocumentTabs.SelectedItem = session.TabItem;
            if (!await WaitUntilAsync(
                    () => ReferenceEquals(ActiveSession, session),
                    TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException(
                    "The transfer fixture could not activate its drawing.");
            }
            RequestAutomationEdit(session, "multi-window-moved-edited");
            if (!await WaitUntilAsync(
                    () => session.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The transferred drawing did not become dirty.");
            }
            DocumentTabs.SelectedItem = sourceSession.TabItem;
            if (!await WaitUntilAsync(
                    () => ReferenceEquals(ActiveSession, sourceSession),
                    TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException(
                    "The transfer fixture could not return focus to the source drawing.");
            }
            var destination = workspaceCoordinator.CreateWindow(
                activate: true,
                createInitialTab: false);
            await Task.Delay(250);
            var movedToDestination = workspaceCoordinator.MoveSession(
                this,
                session,
                destination);
            var firstMoveFailures = new List<string>();
            if (!movedToDestination) firstMoveFailures.Add("move rejected");
            if (sessions.Contains(session)) firstMoveFailures.Add("source retained session");
            if (!destination.OpenSessions.Contains(session)) firstMoveFailures.Add("destination missing session");
            if (!ReferenceEquals(session.CoreWebView, originalCoreWebView)) firstMoveFailures.Add("WebView identity changed");
            if (session.TabOrigin != originalOrigin) firstMoveFailures.Add("origin changed");
            if (!session.IsDirty) firstMoveFailures.Add("dirty state lost");
            if (session.IsMoving) firstMoveFailures.Add("move flag remained set");
            if (!session.IsReady) firstMoveFailures.Add("session stopped being ready");
            if (session.ExternalFileWatcher is null) firstMoveFailures.Add("watcher missing");
            if (ReferenceEquals(session.ExternalFileWatcher, sourceWatcher)) firstMoveFailures.Add("watcher was not rebound");
            if (firstMoveFailures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The live drawing did not move into the second window intact: {string.Join(", ", firstMoveFailures)}.");
            }

            RequestSessionSave(session);
            if (!await WaitUntilAsync(
                    () => !session.IsDirty &&
                        FileContains(movedPath, "multi-window-moved-edited"),
                    TimeSpan.FromSeconds(20)) ||
                !string.Equals(
                    await File.ReadAllTextAsync(sourcePath),
                    sourceInitial,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Saving in the destination window changed the wrong file.");
            }

            RequestAutomationEdit(sourceSession, "multi-window-source-edited");
            if (!await WaitUntilAsync(
                    () => sourceSession.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The source-window drawing did not become dirty.");
            }
            RequestSessionSave(sourceSession);
            if (!await WaitUntilAsync(
                    () => !sourceSession.IsDirty &&
                        FileContains(sourcePath, "multi-window-source-edited"),
                    TimeSpan.FromSeconds(20)) ||
                !FileContains(movedPath, "multi-window-moved-edited"))
            {
                throw new InvalidOperationException(
                    "Saving in the source window changed the destination file.");
            }

            RequestAutomationEdit(session, "multi-window-save-as-edited");
            if (!await WaitUntilAsync(
                    () => session.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The cross-window Save As fixture did not become dirty.");
            }
            session.DocumentService.SaveFileOverrideForSmoke =
                await StorageFile.GetFileFromPathAsync(saveAsPath);
            RequestSessionSave(session, reason: "saveAs");
            if (!await WaitUntilAsync(
                    () => !session.IsDirty &&
                        DesktopDocumentPath.Equals(
                            session.DocumentService.DocumentPath,
                            saveAsPath) &&
                        FileContains(saveAsPath, "multi-window-save-as-edited"),
                    TimeSpan.FromSeconds(20)) ||
                !FileContains(sourcePath, "multi-window-source-edited"))
            {
                throw new InvalidOperationException(
                    "Save As did not remain isolated to the destination window.");
            }

            var externallyModified = CreateDocumentSafetyScene(
                "multi-window-external-modified",
                300);
            await File.WriteAllTextAsync(saveAsPath, externallyModified);
            await destination.CheckExternalFileStateAsync(session, showPrompt: false);
            if (session.ExternalFileState != ExternalFileState.Modified ||
                sourceSession.ExternalFileState != ExternalFileState.None)
            {
                throw new InvalidOperationException(
                    "An external modification crossed window ownership boundaries.");
            }
            await destination.ReloadExternalFileAsync(session);
            if (!await WaitUntilAsync(
                    () => session.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The destination window could not reload its modified file.");
            }

            File.Move(saveAsPath, renamedPath, overwrite: true);
            await destination.CheckExternalFileStateAsync(session, showPrompt: false);
            if (session.ExternalFileState is not
                (ExternalFileState.Moved or ExternalFileState.Deleted))
            {
                throw new InvalidOperationException(
                    "The destination window did not detect an external move.");
            }
            File.Move(renamedPath, saveAsPath, overwrite: true);
            await session.DocumentService.RestoreActiveFileAsync(saveAsPath);
            destination.WatchExternalFile(session, saveAsPath);
            session.ExternalFileState = ExternalFileState.None;

            var restoredContent = await File.ReadAllTextAsync(saveAsPath);
            File.Delete(saveAsPath);
            await destination.CheckExternalFileStateAsync(session, showPrompt: false);
            if (session.ExternalFileState != ExternalFileState.Deleted)
            {
                throw new InvalidOperationException(
                    "The destination window did not detect external deletion.");
            }
            await File.WriteAllTextAsync(saveAsPath, restoredContent);
            await session.DocumentService.RestoreActiveFileAsync(saveAsPath);
            destination.WatchExternalFile(session, saveAsPath);
            session.ExternalFileState = ExternalFileState.None;

            RequestAutomationEdit(session, "multi-window-recovery-edited");
            if (!await WaitUntilAsync(
                    () => session.IsDirty && session.RecoveryUpdatedAt is not null,
                    TimeSpan.FromSeconds(20)) ||
                await destination.recoverySnapshotStore.LoadAsync(
                    originalRecoveryId) is not { } recoveryContent ||
                !recoveryContent.Contains(
                    "multi-window-recovery-edited",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The destination window did not persist the transferred session recovery snapshot.");
            }

            if (!workspaceCoordinator.MoveSession(destination, session, this, 0) ||
                !sessions.Contains(session) ||
                destination.OpenSessions.Contains(session) ||
                !ReferenceEquals(session.CoreWebView, originalCoreWebView) ||
                session.TabOrigin != originalOrigin ||
                !session.IsDirty ||
                session.RecoveryId != originalRecoveryId ||
                session.DetachWindowHandlers is null ||
                session.DetachWebViewHandlers is null ||
                session.DetachEditorHandlers is null ||
                session.ExternalFileWatcher is null)
            {
                throw new InvalidOperationException(
                    "The drawing did not move back to its source window intact.");
            }

            if (await recoverySnapshotStore.LoadAsync(originalRecoveryId) is not
                    { } transferredRecovery ||
                !transferredRecovery.Contains(
                    "multi-window-recovery-edited",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Recovery identity or content was lost during transfer.");
            }
            RequestSessionSave(session);
            if (!await WaitUntilAsync(
                    () => !session.IsDirty &&
                        FileContains(saveAsPath, "multi-window-recovery-edited"),
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The recovered transferred session did not save to its owning file.");
            }

            await Task.Delay(100);
            if (workspaceCoordinator.Windows.Count != originalWindowCount)
            {
                throw new InvalidOperationException(
                    "The empty transfer destination was not released.");
            }

            for (var iteration = 0; iteration < 2; iteration++)
            {
                var repeatedDestination = workspaceCoordinator.CreateWindow(
                    activate: true,
                    createInitialTab: false);
                await Task.Delay(100);
                if (!workspaceCoordinator.MoveSession(
                        this,
                        session,
                        repeatedDestination) ||
                    !workspaceCoordinator.MoveSession(
                        repeatedDestination,
                        session,
                        this,
                        0) ||
                    !ReferenceEquals(session.CoreWebView, originalCoreWebView) ||
                    session.TabOrigin != originalOrigin ||
                    session.DetachWindowHandlers is null ||
                    session.DetachWebViewHandlers is null ||
                    session.DetachEditorHandlers is null ||
                    session.ExternalFileWatcher is null)
                {
                    throw new InvalidOperationException(
                        "A repeated live transfer leaked or duplicated session resources.");
                }
                await Task.Delay(100);
            }

            if (workspaceCoordinator.Windows.Count != originalWindowCount)
            {
                throw new InvalidOperationException(
                    "A repeated transfer leaked an empty window.");
            }

            Title = "Excalidraw Desktop — Multi-window smoke passed";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Title = $"Excalidraw Desktop — Multi-window smoke failed: {exception.Message}";
        }
        finally
        {
            multiWindowInitializationRelease?.TrySetResult();
            multiWindowInitializationRelease = null;
            foreach (var openSession in workspaceCoordinator.Windows
                .SelectMany(window => window.OpenSessions))
            {
                if (paths.Any(path => DesktopDocumentPath.Equals(
                    openSession.DocumentService.DocumentPath,
                    path)))
                {
                    openSession.DetachExternalFileWatcher();
                }
            }
            foreach (var path in paths.Where(File.Exists))
            {
                File.Delete(path);
            }
        }
    }

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
                !DocumentTabs.CanTearOutTabs ||
                WindowDragRegion.ActualWidth <= 0 ||
                !string.Equals(
                    FileMenu.Title?.ToString(),
                    DesktopResources.Get("FileMenu.Title", "File"),
                    StringComparison.Ordinal) ||
                RecentFilesMenu.Items.Count == 0 ||
                !string.Equals(
                    SaveMenuItem.Text,
                    DesktopResources.Get("SaveMenuItem.Text", "Save"),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    SaveAsMenuItem.Text,
                    DesktopResources.Get("SaveAsMenuItem.Text", "Save as…"),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    ExportPngMenuItem.Text,
                    DesktopResources.Get(
                        "ExportPngMenuItem.Text",
                        "Export whole drawing as PNG…"),
                    StringComparison.Ordinal) ||
                SettingsButton.ActualWidth <= 0 ||
                sessions.Count != 2)
            {
                throw new InvalidOperationException(
                    $"The title-bar layout was not initialized correctly. " +
                    $"Extends={ExtendsContentIntoTitleBar}; Registered={titleBarRegistered}; " +
                    $"Scale={scale}; Insets={TitleBarLeftInset.Width.Value}/{expectedLeftInset}," +
                    $"{TitleBarRightInset.Width.Value}/{expectedRightInset}; " +
                    $"Heights={TitleBarContainer.ActualHeight}/{DocumentTabs.ActualHeight}; " +
                    $"TearOut={DocumentTabs.CanTearOutTabs}; DragWidth={WindowDragRegion.ActualWidth}; " +
                    $"File='{FileMenu.Title}'/'{DesktopResources.Get("FileMenu.Title", "File")}'; " +
                    $"Recent={RecentFilesMenu.Items.Count}; " +
                    $"Save='{SaveMenuItem.Text}'/'{DesktopResources.Get("SaveMenuItem.Text", "Save")}'; " +
                    $"SaveAs='{SaveAsMenuItem.Text}'/'{DesktopResources.Get("SaveAsMenuItem.Text", "Save as…")}'; " +
                    $"Export='{ExportPngMenuItem.Text}'/'{DesktopResources.Get("ExportPngMenuItem.Text", "Export whole drawing as PNG…")}'; " +
                    $"SettingsWidth={SettingsButton.ActualWidth}; Sessions={sessions.Count}.");
            }

            var requiredAccelerators = new[]
            {
                (VirtualKey.T, VirtualKeyModifiers.Control),
                (VirtualKey.N, VirtualKeyModifiers.Control),
                (VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
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
            DocumentTabs.SelectedItem = first.TabItem;
            await Task.Yield();
            UpdateStatusBar(first);
            var expectedStatusDocument =
                first.DocumentService.DocumentPath ?? first.DisplayName;
            if (StatusBar.Visibility != Visibility.Visible ||
                Math.Abs(StatusBar.Height - 32) > 0.75 ||
                !string.Equals(
                    StatusDocumentText.Text,
                    expectedStatusDocument,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(StatusStateText.Text) ||
                StatusStateText.Visibility != Visibility.Visible ||
                StatusActionButton.Visibility != Visibility.Collapsed ||
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(StatusDocumentText)))
            {
                throw new InvalidOperationException(
                    "The native drawing status bar was not initialized correctly.");
            }

            first.ExternalFileState = ExternalFileState.Modified;
            UpdateStatusBar(first);
            if (StatusStateText.Visibility != Visibility.Collapsed ||
                StatusActionButton.Visibility != Visibility.Visible ||
                !string.Equals(
                    StatusActionButton.Content?.ToString(),
                    DesktopResources.Get(
                        "StatusChangedOnDisk",
                        "Changed on disk — Resolve"),
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(StatusActionButton)))
            {
                throw new InvalidOperationException(
                    "The native status action was not exposed for a file conflict.");
            }
            first.ExternalFileState = ExternalFileState.None;
            UpdateStatusBar(first);

            first.IsDirty = true;
            UpdateTabHeader(first);
            if (!AutomationProperties.GetName(first.TabItem).Contains(
                DesktopResources.Get("TabStateUnsaved", "unsaved changes"),
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
                settingsTabItem.CanDrag ||
                !DocumentTabs.TabItems.Contains(settingsTabItem) ||
                FindSession(settingsTabItem) is not null ||
                AppSettingsPage.Visibility != Visibility.Visible ||
                EditorHost.Visibility != Visibility.Collapsed ||
                StatusBar.Visibility != Visibility.Collapsed ||
                Title != DesktopResources.Get(
                    "SettingsWindowTitle",
                    "Settings — Excalidraw Desktop"))
            {
                throw new InvalidOperationException(
                    "The native Settings tab did not replace the editor surface correctly.");
            }
            HideSettingsPage();
            if (settingsPageVisible ||
                settingsTabItem is not null ||
                AppSettingsPage.Visibility != Visibility.Collapsed ||
                EditorHost.Visibility != Visibility.Visible ||
                StatusBar.Visibility != Visibility.Visible)
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

            var closingRecoveryId = second.RecoveryId;
            await recoverySnapshotStore.SaveAsync(
                closingRecoveryId,
                "{\"type\":\"excalidraw\",\"version\":2,\"elements\":[],\"appState\":{},\"files\":{}}");
            if (await recoverySnapshotStore.LoadAsync(closingRecoveryId) is null)
            {
                throw new InvalidOperationException(
                    "The recovery cleanup probe was not written.");
            }

            if (sessions.Count != 2 ||
                !ReferenceEquals(sessions[1], first) ||
                !ReferenceEquals(ActiveSession, first))
            {
                throw new InvalidOperationException(
                    "Tab ordering regressed in the custom title bar.");
            }

            QueueCloseSession(second);
            var queuedCloseDeadline =
                DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (sessions.Contains(second) &&
                DateTimeOffset.UtcNow < queuedCloseDeadline)
            {
                await Task.Delay(25);
            }
            if (sessions.Contains(second) ||
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

            var recoveryCleanupDeadline =
                DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (await recoverySnapshotStore.LoadAsync(closingRecoveryId) is not null &&
                DateTimeOffset.UtcNow < recoveryCleanupDeadline)
            {
                await Task.Delay(50);
            }
            if (await recoverySnapshotStore.LoadAsync(closingRecoveryId) is not null)
            {
                throw new InvalidOperationException(
                    "Closing a tab did not delete its recovery snapshot.");
            }

            QueueCloseSession(first);
            var replacementDeadline =
                DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
            while ((sessions.Contains(first) ||
                    sessions.Count != 1 ||
                    !sessions[0].IsReady) &&
                DateTimeOffset.UtcNow < replacementDeadline)
            {
                await Task.Delay(50);
            }
            if (sessions.Contains(first) ||
                sessions.Count != 1 ||
                ReferenceEquals(sessions[0], first) ||
                !sessions[0].IsReady)
            {
                throw new InvalidOperationException(
                    "Closing the final untitled tab did not create a ready replacement tab.");
            }

            Title = "Excalidraw Desktop — Title bar smoke passed";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await File.WriteAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "titlebar-smoke-error.txt"),
                exception.ToString());
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
        var largeScenePath = Path.Combine(
            AppContext.BaseDirectory,
            "suspension-large-scene.excalidraw");
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

            await File.WriteAllTextAsync(
                largeScenePath,
                CreateLargeLifecycleScene(elementCount: 500));
            AttachDocumentToSession(
                unloading,
                await unloading.DocumentService.OpenPathAsync(largeScenePath),
                select: false);
            if (!await WaitUntilAsync(
                    () => unloading.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)) ||
                unloading.IsDirty)
            {
                throw new InvalidOperationException(
                    "The large embedded-image drawing did not load cleanly.");
            }

            var liveCoreBeforeFailure = unloading.CoreWebView;
            await File.WriteAllTextAsync(largeScenePath, "{ invalid lifecycle fixture");
            unloading.InactiveSince = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(16);
            if (await UnloadSessionAsync(unloading, requireIdle: true) ||
                unloading.IsUnloaded ||
                !unloading.IsReady ||
                unloading.CoreWebView is null ||
                !ReferenceEquals(unloading.CoreWebView, liveCoreBeforeFailure) ||
                !unloading.Content.HasEditor ||
                string.IsNullOrWhiteSpace(unloading.LastLifecycleFailure))
            {
                throw new InvalidOperationException(
                    "A failed unload did not leave the live editor available for recovery.");
            }
            await File.WriteAllTextAsync(
                largeScenePath,
                CreateLargeLifecycleScene(elementCount: 500));
            await unloading.DocumentService.RestoreActiveFileAsync(largeScenePath);
            unloading.ExternalFileState = ExternalFileState.None;
            unloading.LastLifecycleFailure = null;

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

            for (var cycle = 0; cycle < 3; cycle++)
            {
                DocumentTabs.SelectedItem = sleeping.TabItem;
                if (!await WaitUntilAsync(
                        () => ReferenceEquals(ActiveSession, sleeping) &&
                            unloading.Content.Visibility != Visibility.Visible,
                        TimeSpan.FromSeconds(5)))
                {
                    throw new InvalidOperationException(
                        $"Unload cycle {cycle + 1} could not deactivate the target tab.");
                }
                unloading.InactiveSince =
                    DateTimeOffset.UtcNow - TimeSpan.FromMinutes(16);
                var suspensionReady = cycle != 0 ||
                    await SuspendSessionAsync(unloading, requireIdle: false);
                var unloadedForCycle = suspensionReady &&
                    await UnloadSessionAsync(unloading, requireIdle: true);
                if (!suspensionReady ||
                    !unloadedForCycle ||
                    !unloading.IsUnloaded ||
                    unloading.CoreWebView is not null ||
                    unloading.DetachWebViewHandlers is not null ||
                    unloading.DetachEditorHandlers is not null ||
                    unloading.Content.HasEditor ||
                    unloading.LastLifecycleFailure is not null)
                {
                    throw new InvalidOperationException(
                        $"C{cycle + 1}: ready={unloading.IsReady}, dirty={unloading.IsDirty}, " +
                        $"suspended={unloading.IsSuspended}, unloaded={unloading.IsUnloaded}, " +
                        $"core={unloading.CoreWebView is not null}, " +
                        $"pending={unloading.PendingDocumentLoad is not null}; " +
                        unloading.LastLifecycleFailure);
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
                        $"Unload cycle {cycle + 1} did not recreate its isolated editor.");
                }

                var restoredState = await ReadSmokeStateAsync(unloading);
                if (restoredState.ElementIds.Length != 501 ||
                    !restoredState.ElementIds.Contains("lifecycle-embedded-image") ||
                    !restoredState.FileIds.Contains("lifecycle-image-file"))
                {
                    throw new InvalidOperationException(
                        $"Unload cycle {cycle + 1} lost the large drawing or embedded image.");
                }
            }

            Title = "Excalidraw Desktop — Suspension smoke passed";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Title = $"Excalidraw Desktop — Suspension smoke failed: {exception.Message}";
        }
        finally
        {
            foreach (var session in sessions.Where(session =>
                string.Equals(
                    session.DocumentService.DocumentPath,
                    largeScenePath,
                    StringComparison.OrdinalIgnoreCase)))
            {
                session.DetachExternalFileWatcher();
            }
            if (File.Exists(largeScenePath))
            {
                File.Delete(largeScenePath);
            }
        }
    }

    private static string CreateLargeLifecycleScene(int elementCount)
    {
        var elements = new List<Dictionary<string, object?>>(elementCount + 1);
        for (var index = 0; index < elementCount; index++)
        {
            elements.Add(new Dictionary<string, object?>
            {
                ["id"] = $"lifecycle-rectangle-{index}",
                ["type"] = "rectangle",
                ["x"] = (index % 25) * 180,
                ["y"] = (index / 25) * 140,
                ["width"] = 140,
                ["height"] = 100,
                ["angle"] = 0,
                ["strokeColor"] = "#1e1e1e",
                ["backgroundColor"] = index % 2 == 0 ? "#a5d8ff" : "#b2f2bb",
                ["fillStyle"] = "solid",
                ["strokeWidth"] = 2,
                ["strokeStyle"] = "solid",
                ["roughness"] = 1,
                ["opacity"] = 100,
                ["groupIds"] = Array.Empty<string>(),
                ["frameId"] = null,
                ["roundness"] = new { type = 3 },
                ["seed"] = 1000 + index,
                ["version"] = 1,
                ["versionNonce"] = 2000 + index,
                ["isDeleted"] = false,
                ["boundElements"] = Array.Empty<object>(),
                ["updated"] = 1,
                ["link"] = null,
                ["locked"] = false,
            });
        }
        elements.Add(new Dictionary<string, object?>
        {
            ["id"] = "lifecycle-embedded-image",
            ["type"] = "image",
            ["x"] = 200,
            ["y"] = 200,
            ["width"] = 128,
            ["height"] = 128,
            ["angle"] = 0,
            ["strokeColor"] = "transparent",
            ["backgroundColor"] = "transparent",
            ["fillStyle"] = "solid",
            ["strokeWidth"] = 1,
            ["strokeStyle"] = "solid",
            ["roughness"] = 0,
            ["opacity"] = 100,
            ["groupIds"] = Array.Empty<string>(),
            ["frameId"] = null,
            ["roundness"] = null,
            ["seed"] = 9001,
            ["version"] = 1,
            ["versionNonce"] = 9002,
            ["isDeleted"] = false,
            ["boundElements"] = Array.Empty<object>(),
            ["updated"] = 1,
            ["link"] = null,
            ["locked"] = false,
            ["fileId"] = "lifecycle-image-file",
            ["status"] = "saved",
            ["scale"] = new[] { 1, 1 },
            ["crop"] = null,
        });
        return JsonSerializer.Serialize(new
        {
            type = "excalidraw",
            version = 2,
            source = "excalidraw-desktop-lifecycle-smoke",
            elements,
            appState = new { viewBackgroundColor = "#ffffff" },
            files = new Dictionary<string, object>
            {
                ["lifecycle-image-file"] = new
                {
                    id = "lifecycle-image-file",
                    dataURL = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZQmcAAAAASUVORK5CYII=",
                    mimeType = "image/png",
                    created = 1,
                    lastRetrieved = 1,
                },
            },
        });
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
            await ConfigureSmokeStateAsync(
                originallyFirst,
                "first-state-element",
                scrollX: 111,
                scrollY: -222,
                zoom: 1.5,
                indexedDbValue: "first-indexed-db");
            await ConfigureSmokeStateAsync(
                originallySecond,
                "second-state-element",
                scrollX: -333,
                scrollY: 444,
                zoom: 0.75,
                indexedDbValue: "second-indexed-db");

            DocumentTabs.SelectedItem = originallyFirst.TabItem;
            await Task.Delay(100);
            var firstState = await ReadSmokeStateAsync(originallyFirst);
            DocumentTabs.SelectedItem = originallySecond.TabItem;
            await Task.Delay(100);
            var secondState = await ReadSmokeStateAsync(originallySecond);
            if (!SmokeStateMatches(
                    firstState,
                    "first-state-element",
                    111,
                    -222,
                    1.5,
                    "first-indexed-db") ||
                !SmokeStateMatches(
                    secondState,
                    "second-state-element",
                    -333,
                    444,
                    0.75,
                    "second-indexed-db"))
            {
                throw new InvalidOperationException(
                    "Scene, viewport, selection, or IndexedDB state crossed tab origins.");
            }

            DocumentTabs.SelectedItem = originallyFirst.TabItem;
            originallyFirst.Content.Editor.Focus(FocusState.Programmatic);
            var canvasFocused = await originallyFirst.CoreWebView!.ExecuteScriptAsync(
                "window.__EXCALIDRAW_DESKTOP_SMOKE__.focusCanvas()");
            if (canvasFocused != "true")
            {
                throw new InvalidOperationException(
                    "The active editor canvas could not receive focus for undo validation.");
            }
            var firstAfterUndo = await ReadSmokeStateAsync(originallyFirst);
            for (var attempt = 0;
                attempt < 4 &&
                    firstAfterUndo.ElementIds.Contains("first-state-element");
                attempt++)
            {
                await originallyFirst.CoreWebView!.CallDevToolsProtocolMethodAsync(
                    "Input.dispatchKeyEvent",
                    """{"type":"rawKeyDown","modifiers":2,"key":"z","code":"KeyZ","windowsVirtualKeyCode":90,"nativeVirtualKeyCode":90}""");
                await originallyFirst.CoreWebView.CallDevToolsProtocolMethodAsync(
                    "Input.dispatchKeyEvent",
                    """{"type":"keyUp","modifiers":2,"key":"z","code":"KeyZ","windowsVirtualKeyCode":90,"nativeVirtualKeyCode":90}""");
                await Task.Delay(100);
                firstAfterUndo = await ReadSmokeStateAsync(originallyFirst);
            }
            var secondAfterFirstUndo = await ReadSmokeStateAsync(originallySecond);
            if (firstAfterUndo.ElementIds.Contains("first-state-element") ||
                !secondAfterFirstUndo.ElementIds.Contains("second-state-element"))
            {
                throw new InvalidOperationException(
                    $"Undo history was not isolated to the active editor tab. " +
                    $"First=[{string.Join(',', firstAfterUndo.ElementIds)}]; " +
                    $"Second=[{string.Join(',', secondAfterFirstUndo.ElementIds)}].");
            }

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
            Title = $"Excalidraw Desktop — Tab smoke failed: {exception.Message}";
        }
    }

    private static async Task ConfigureSmokeStateAsync(
        DocumentSession session,
        string elementId,
        double scrollX,
        double scrollY,
        double zoom,
        string indexedDbValue)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        string readiness;
        do
        {
            readiness = await session.CoreWebView!.ExecuteScriptAsync(
                "typeof window.__EXCALIDRAW_DESKTOP_SMOKE__");
            if (readiness == "\"object\"")
            {
                break;
            }
            await Task.Delay(50);
        }
        while (DateTimeOffset.UtcNow < deadline);
        if (readiness != "\"object\"")
        {
            throw new InvalidOperationException(
                "The scoped editor smoke API was not installed.");
        }

        var options = JsonSerializer.Serialize(new
        {
            elementId,
            scrollX,
            scrollY,
            zoom,
            indexedDbValue,
        });
        var configured = await session.CoreWebView!.ExecuteScriptAsync(
            $"window.__EXCALIDRAW_DESKTOP_SMOKE__.configureState({options}); true");
        if (configured != "true")
        {
            throw new InvalidOperationException(
                "The editor smoke state could not be configured.");
        }

        SmokeState state;
        do
        {
            await Task.Delay(50);
            state = await ReadSmokeStateAsync(session);
        }
        while (state.IndexedDbValue != indexedDbValue &&
            DateTimeOffset.UtcNow < deadline);
        if (state.IndexedDbValue != indexedDbValue)
        {
            throw new InvalidOperationException(
                "The editor IndexedDB state was not written and read back.");
        }
    }

    private static async Task<SmokeState> ReadSmokeStateAsync(
        DocumentSession session)
    {
        var encoded = await session.CoreWebView!.ExecuteScriptAsync(
            "JSON.stringify(window.__EXCALIDRAW_DESKTOP_SMOKE__.readState())");
        var json = JsonSerializer.Deserialize<string>(encoded) ??
            throw new InvalidOperationException("The editor returned no smoke state.");
        return JsonSerializer.Deserialize<SmokeState>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
            throw new InvalidOperationException("The editor smoke state was invalid.");
    }

    private static bool SmokeStateMatches(
        SmokeState state,
        string elementId,
        double scrollX,
        double scrollY,
        double zoom,
        string indexedDbValue) =>
        state.ElementIds.Length == 2 &&
        state.ElementIds.Contains($"{elementId}-baseline") &&
        state.ElementIds.Contains(elementId) &&
        Math.Abs(state.ScrollX - scrollX) < 0.01 &&
        Math.Abs(state.ScrollY - scrollY) < 0.01 &&
        Math.Abs(state.Zoom - zoom) < 0.01 &&
        state.SelectedElementIds.SequenceEqual([elementId]) &&
        state.IndexedDbValue == indexedDbValue;

    private sealed record SmokeState(
        string[] ElementIds,
        string[] FileIds,
        double ScrollX,
        double ScrollY,
        double Zoom,
        string[] SelectedElementIds,
        string? IndexedDbValue);
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

#if DEBUG
        if (session.ForceDirtyForSmoke && !isDirty)
        {
            return;
        }
#endif
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

        session.CoreWebView.PostWebMessageAsJson(
            BridgeEventJson.Create(
                "app.automationEditRequested",
                new { elementId = $"recovery-{session.RecoveryId}" }));
        await Task.CompletedTask;
    }

    private void TryRunImageExportSmoke()
    {
        if (!runImageExportSmoke || imageExportSmokeStarted)
        {
            return;
        }

        if (sessions.FirstOrDefault(candidate => !candidate.IsReady) is { } pending)
        {
            DocumentTabs.SelectedItem = pending.TabItem;
            return;
        }

        if (ActiveSession is { CoreWebView: not null } session)
        {
            imageExportSmokeStarted = true;
            _ = RunImageExportSmokeAsync(session);
        }
    }

    private void TryRunDocumentSafetySmoke()
    {
        if (!runDocumentSafetySmoke || documentSafetySmokeStarted)
        {
            return;
        }

        if (sessions.Count != 2)
        {
            return;
        }

        if (sessions.FirstOrDefault(candidate => !candidate.IsReady) is { } pending)
        {
            DocumentTabs.SelectedItem = pending.TabItem;
            return;
        }

        documentSafetySmokeStarted = true;
        _ = RunDocumentSafetySmokeAsync();
    }

    private async Task RunDocumentSafetySmokeAsync()
    {
        var paths = new List<string>();
        try
        {
            var first = sessions[0];
            var second = sessions[1];
            var firstPath = Path.Combine(
                AppContext.BaseDirectory,
                "document-safety-first.excalidraw");
            var secondPath = Path.Combine(
                AppContext.BaseDirectory,
                "document-safety-second.excalidraw");
            var movedPath = Path.Combine(
                AppContext.BaseDirectory,
                "document-safety-first-moved.excalidraw");
            var saveAsPath = Path.Combine(
                AppContext.BaseDirectory,
                "document-safety-conflict-save-as.excalidraw");
            paths.Add(firstPath);
            paths.Add(secondPath);
            paths.Add(movedPath);
            paths.Add(saveAsPath);

            var firstInitial = CreateDocumentSafetyScene("first-initial", -200);
            var secondInitial = CreateDocumentSafetyScene("second-initial", 200);
            var firstEdited = CreateDocumentSafetyScene("first-edited", -250);
            var secondEdited = CreateDocumentSafetyScene("second-edited", 250);
            await File.WriteAllTextAsync(firstPath, firstInitial);
            await File.WriteAllTextAsync(secondPath, secondInitial);

            AttachDocumentToSession(
                first,
                await first.DocumentService.OpenPathAsync(firstPath),
                select: true);
            AttachDocumentToSession(
                second,
                await second.DocumentService.OpenPathAsync(secondPath),
                select: false);
            if (!await WaitUntilAsync(
                    () => first.PendingDocumentLoad is null &&
                        second.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The document-safety drawings did not finish loading.");
            }

            await LoadAutomationSceneAsync(first, firstEdited);
            RequestSessionSave(first);
            if (!await WaitUntilAsync(
                    () => FileContains(firstPath, "first-edited"),
                    TimeSpan.FromSeconds(10)) ||
                !string.Equals(
                    await File.ReadAllTextAsync(secondPath),
                    secondInitial,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Saving the first tab changed the wrong document.");
            }

            await LoadAutomationSceneAsync(second, secondEdited);
            RequestSessionSave(second);
            if (!await WaitUntilAsync(
                    () => FileContains(secondPath, "second-edited"),
                    TimeSpan.FromSeconds(10)) ||
                !FileContains(firstPath, "first-edited"))
            {
                throw new InvalidOperationException(
                    "Saving the second tab changed the wrong document.");
            }

            if (DesktopDocumentPath.Equals(
                    first.DocumentService.DocumentPath,
                    second.DocumentService.DocumentPath) ||
                first.TabOrigin == second.TabOrigin ||
                ReferenceEquals(first.Dispatcher, second.Dispatcher))
            {
                throw new InvalidOperationException(
                    "Document handles, origins, or bridge dispatchers crossed session boundaries.");
            }

            await File.WriteAllTextAsync(
                firstPath,
                CreateDocumentSafetyScene("first-external", -300));
            Title = "Excalidraw Desktop — Document safety smoke: choose Reload";
            await CheckExternalFileStateAsync(first, showPrompt: true);
            if (!await WaitUntilAsync(
                    () => first.PendingDocumentLoad is null &&
                        first.ExternalFileState == ExternalFileState.None,
                    TimeSpan.FromSeconds(20)) ||
                !(await ReadSmokeStateAsync(first)).ElementIds.Contains(
                    "first-external"))
            {
                throw new InvalidOperationException(
                    "Reload did not adopt the externally modified drawing.");
            }

            RequestAutomationEdit(first, "first-conflict-save-as");
            if (!await WaitUntilAsync(
                    () => first.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The Save As conflict fixture did not become dirty.");
            }
            var originalExternalContent = CreateDocumentSafetyScene(
                "first-external-before-save-as",
                -350);
            await File.WriteAllTextAsync(firstPath, originalExternalContent);
            await File.WriteAllTextAsync(
                saveAsPath,
                CreateDocumentSafetyScene("save-as-placeholder", 0));
            first.DocumentService.SaveFileOverrideForSmoke =
                await StorageFile.GetFileFromPathAsync(saveAsPath);
            Title = "Excalidraw Desktop — Document safety smoke: choose Save As";
            await CheckExternalFileStateAsync(first, showPrompt: true);
            if (!await WaitUntilAsync(
                    () => DesktopDocumentPath.Equals(
                            first.DocumentService.DocumentPath,
                            saveAsPath) &&
                        !first.IsDirty &&
                        FileContains(saveAsPath, "first-conflict-save-as"),
                    TimeSpan.FromSeconds(20)) ||
                !string.Equals(
                    await File.ReadAllTextAsync(firstPath),
                    originalExternalContent,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Save As did not preserve the external file and write the chosen file.");
            }

            RequestAutomationEdit(first, "first-keep-editing");
            if (!await WaitUntilAsync(
                    () => first.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The Keep Editing conflict fixture did not become dirty.");
            }
            var keepEditingDiskContent = CreateDocumentSafetyScene(
                "first-external-keep-editing",
                -400);
            await File.WriteAllTextAsync(saveAsPath, keepEditingDiskContent);
            Title = "Excalidraw Desktop — Document safety smoke: choose Keep Editing";
            await CheckExternalFileStateAsync(first, showPrompt: true);
            if (!first.IsDirty ||
                first.ExternalFileState != ExternalFileState.Modified ||
                !DesktopDocumentPath.Equals(
                    first.DocumentService.DocumentPath,
                    saveAsPath) ||
                !string.Equals(
                    await File.ReadAllTextAsync(saveAsPath),
                    keepEditingDiskContent,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Keep Editing did not preserve the dirty editor and external file.");
            }

            var refreshedFirst = await first.DocumentService.OpenPathAsync(saveAsPath);
            AttachDocumentToSession(first, refreshedFirst, select: true);
            if (!await WaitUntilAsync(
                    () => first.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The conflict-resolution drawing could not be refreshed.");
            }

            File.Move(saveAsPath, movedPath, overwrite: true);
            if (!await WaitUntilAsync(
                    () => first.ExternalFileState is
                        ExternalFileState.Moved or ExternalFileState.Deleted,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "An externally moved drawing was not detected.");
            }

            File.Delete(secondPath);
            if (!await WaitUntilAsync(
                    () => second.ExternalFileState == ExternalFileState.Deleted,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "An externally deleted drawing was not detected.");
            }

            Title = "Excalidraw Desktop — Document safety smoke passed";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Title = $"Excalidraw Desktop — Document safety smoke failed: {exception.Message}";
        }
        finally
        {
            foreach (var session in sessions)
            {
                session.DetachExternalFileWatcher();
            }
            foreach (var path in paths.Where(File.Exists))
            {
                File.Delete(path);
            }
        }
    }

    private async Task LoadAutomationSceneAsync(
        DocumentSession session,
        string content)
    {
        session.CoreWebView!.PostWebMessageAsJson(
            BridgeEventJson.Create(
                "document.loadRequested",
                new
                {
                    fileName = session.DisplayName,
                    content,
                    isRecovery = false,
                }));
        await Task.Delay(500);
    }

    private static string CreateDocumentSafetyScene(string id, int x) =>
        JsonSerializer.Serialize(new
        {
            type = "excalidraw",
            version = 2,
            source = "excalidraw-desktop-document-safety-smoke",
            elements = new[]
            {
                new
                {
                    id,
                    type = "rectangle",
                    x,
                    y = 0,
                    width = 100,
                    height = 80,
                    angle = 0,
                    strokeColor = "#1e1e1e",
                    backgroundColor = "#a5d8ff",
                    fillStyle = "solid",
                    strokeWidth = 2,
                    strokeStyle = "solid",
                    roughness = 1,
                    opacity = 100,
                    groupIds = Array.Empty<string>(),
                    frameId = (string?)null,
                    roundness = new { type = 3 },
                    seed = 101,
                    version = 1,
                    versionNonce = 201,
                    isDeleted = false,
                    boundElements = Array.Empty<object>(),
                    updated = 1,
                    link = (string?)null,
                    locked = false,
                },
            },
            appState = new { viewBackgroundColor = "#ffffff" },
            files = new { },
        });

    private static bool FileContains(string path, string value)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Contains(
                value,
                StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void RequestSessionSave(
        DocumentSession session,
        string reason = "save")
    {
        session.CoreWebView!.PostWebMessageAsJson(
            BridgeEventJson.Create(
                "document.saveRequested",
                new { reason }));
    }

    private static async Task<bool> WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(pollInterval ?? TimeSpan.FromMilliseconds(50));
        }
        while (DateTimeOffset.UtcNow < deadline);
        return condition();
    }

    private void TryRunCloseDecisionsSmoke()
    {
        if (!runCloseDecisionsSmoke || closeDecisionsSmokeStarted ||
            sessions.Count != 2)
        {
            return;
        }
        if (sessions.FirstOrDefault(candidate => !candidate.IsReady) is { } pending)
        {
            DocumentTabs.SelectedItem = pending.TabItem;
            return;
        }

        closeDecisionsSmokeStarted = true;
        _ = RunCloseDecisionsSmokeAsync();
    }

    private async Task RunCloseDecisionsSmokeAsync()
    {
        var savedPath = Path.Combine(
            AppContext.BaseDirectory,
            "close-decisions-save.excalidraw");
        var windowClosePaths = Enumerable.Range(1, 3)
            .Select(index => Path.Combine(
                AppContext.BaseDirectory,
                $"window-close-save-{index}.excalidraw"))
            .ToArray();
        try
        {
            var cancelAndDiscardSession = sessions[0];
            var saveSession = sessions[1];
            var initialScene = CreateDocumentSafetyScene("close-save-initial", 0);
            await File.WriteAllTextAsync(savedPath, initialScene);
            AttachDocumentToSession(
                saveSession,
                await saveSession.DocumentService.OpenPathAsync(savedPath),
                select: false);
            if (!await WaitUntilAsync(
                    () => saveSession.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The close-save drawing did not finish loading.");
            }

            RequestAutomationEdit(cancelAndDiscardSession, "close-cancel-discard");
            if (!await WaitUntilAsync(
                    () => cancelAndDiscardSession.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The cancel/discard drawing did not become dirty.");
            }

            Title = "Excalidraw Desktop — Close decisions smoke: choose Cancel";
            if (await RequestCloseSessionAsync(cancelAndDiscardSession) ||
                !sessions.Contains(cancelAndDiscardSession) ||
                !cancelAndDiscardSession.IsDirty)
            {
                throw new InvalidOperationException(
                    "Cancel did not preserve the dirty drawing.");
            }

            Title = "Excalidraw Desktop — Close decisions smoke: choose Discard";
            if (!await RequestCloseSessionAsync(cancelAndDiscardSession) ||
                sessions.Contains(cancelAndDiscardSession))
            {
                throw new InvalidOperationException(
                    "Discard did not close the dirty drawing.");
            }

            RequestAutomationEdit(saveSession, "close-save-edited");
            if (!await WaitUntilAsync(
                    () => saveSession.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The close-save drawing did not become dirty.");
            }

            Title = "Excalidraw Desktop — Close decisions smoke: choose Save";
            if (!await RequestCloseSessionAsync(saveSession) ||
                sessions.Contains(saveSession) ||
                !FileContains(savedPath, "close-save-edited"))
            {
                throw new InvalidOperationException(
                    "Save did not write and close the owning drawing.");
            }

            while (sessions.Count < 3)
            {
                CreateTab(select: false);
            }
            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                DocumentTabs.SelectedItem = session.TabItem;
                await InitializeSessionAsync(session);
                await File.WriteAllTextAsync(
                    windowClosePaths[index],
                    CreateDocumentSafetyScene($"window-close-initial-{index}", index));
                AttachDocumentToSession(
                    session,
                    await session.DocumentService.OpenPathAsync(windowClosePaths[index]),
                    select: false);
            }
            if (!await WaitUntilAsync(
                    () => sessions.All(session =>
                        session.IsReady && session.PendingDocumentLoad is null),
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The window-close drawings did not finish loading.");
            }
            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                DocumentTabs.SelectedItem = session.TabItem;
                await Task.Delay(100);
                RequestAutomationEdit(session, $"window-close-edited-{index}");
                if (!await WaitUntilAsync(
                        () => session.IsDirty,
                        TimeSpan.FromSeconds(10)))
                {
                    throw new InvalidOperationException(
                        $"Window-close drawing {index + 1} did not become dirty.");
                }
            }

            Title = "Excalidraw Desktop — Window close smoke: choose Cancel";
            if (await ResolveWindowCloseAsync() ||
                sessions.Any(session => !session.IsDirty))
            {
                throw new InvalidOperationException(
                    "Window-close Cancel did not preserve every dirty drawing.");
            }

            var firstDirtySession = sessions[0];
            Title = "Excalidraw Desktop — Window close smoke: choose Review Tabs";
            if (await ResolveWindowCloseAsync() ||
                !ReferenceEquals(ActiveSession, firstDirtySession) ||
                sessions.Any(session => !session.IsDirty))
            {
                throw new InvalidOperationException(
                    "Review Tabs did not preserve and select the first dirty drawing.");
            }

            Title = "Excalidraw Desktop — Window close smoke: choose Save All";
            if (!await ResolveWindowCloseAsync() ||
                sessions.Any(session => session.IsDirty) ||
                windowClosePaths.Where((path, index) =>
                    !FileContains(path, $"window-close-edited-{index}")).Any())
            {
                throw new InvalidOperationException(
                    "Save All did not save every owning drawing.");
            }

            Title = "Excalidraw Desktop — Close decisions smoke passed";
            await File.WriteAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "close-decisions-smoke.result"),
                "passed");
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Title = $"Excalidraw Desktop — Close decisions smoke failed: {exception.Message}";
            await File.WriteAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "close-decisions-smoke.result"),
                $"failed:{exception.Message}");
        }
        finally
        {
            foreach (var session in sessions)
            {
                session.DetachExternalFileWatcher();
            }
            if (File.Exists(savedPath))
            {
                File.Delete(savedPath);
            }
            foreach (var path in windowClosePaths)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private static void RequestAutomationEdit(
        DocumentSession session,
        string elementId)
    {
        session.CoreWebView!.PostWebMessageAsJson(
            BridgeEventJson.Create(
                "app.automationEditRequested",
                new { elementId }));
    }

    private async Task RunImageExportSmokeAsync(DocumentSession session)
    {
        const string sceneJson = """
            {
              "type": "excalidraw",
              "version": 2,
              "source": "excalidraw-desktop-image-export-smoke",
              "elements": [
                {
                  "id": "left-rectangle",
                  "type": "rectangle",
                  "x": -3500,
                  "y": -900,
                  "width": 180,
                  "height": 120,
                  "angle": 0,
                  "strokeColor": "#1e1e1e",
                  "backgroundColor": "#a5d8ff",
                  "fillStyle": "solid",
                  "strokeWidth": 2,
                  "strokeStyle": "solid",
                  "roughness": 1,
                  "opacity": 100,
                  "groupIds": [],
                  "frameId": null,
                  "roundness": { "type": 3 },
                  "seed": 101,
                  "version": 1,
                  "versionNonce": 201,
                  "isDeleted": false,
                  "boundElements": [],
                  "updated": 1,
                  "link": null,
                  "locked": false
                },
                {
                  "id": "right-rectangle",
                  "type": "rectangle",
                  "x": 3500,
                  "y": 900,
                  "width": 180,
                  "height": 120,
                  "angle": 0,
                  "strokeColor": "#1e1e1e",
                  "backgroundColor": "#b2f2bb",
                  "fillStyle": "solid",
                  "strokeWidth": 2,
                  "strokeStyle": "solid",
                  "roughness": 1,
                  "opacity": 100,
                  "groupIds": [],
                  "frameId": null,
                  "roundness": { "type": 3 },
                  "seed": 102,
                  "version": 1,
                  "versionNonce": 202,
                  "isDeleted": false,
                  "boundElements": [],
                  "updated": 1,
                  "link": null,
                  "locked": false
                },
                {
                  "id": "embedded-image",
                  "type": "image",
                  "x": 0,
                  "y": 0,
                  "width": 128,
                  "height": 128,
                  "angle": 0,
                  "strokeColor": "transparent",
                  "backgroundColor": "transparent",
                  "fillStyle": "solid",
                  "strokeWidth": 1,
                  "strokeStyle": "solid",
                  "roughness": 0,
                  "opacity": 100,
                  "groupIds": [],
                  "frameId": null,
                  "roundness": null,
                  "seed": 103,
                  "version": 1,
                  "versionNonce": 203,
                  "isDeleted": false,
                  "boundElements": [],
                  "updated": 1,
                  "link": null,
                  "locked": false,
                  "fileId": "smoke-image-file",
                  "status": "saved",
                  "scale": [1, 1],
                  "crop": null
                }
              ],
              "appState": { "viewBackgroundColor": "#ffffff" },
              "files": {
                "smoke-image-file": {
                  "id": "smoke-image-file",
                  "dataURL": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZQmcAAAAASUVORK5CYII=",
                  "mimeType": "image/png",
                  "created": 1,
                  "lastRetrieved": 1
                }
              }
            }
            """;

        try
        {
            var scenePath = Path.Combine(
                AppContext.BaseDirectory,
                "image-export-smoke.excalidraw");
            imageExportSmokeOutputPath = Path.Combine(
                AppContext.BaseDirectory,
                "image-export-smoke.png");
            await File.WriteAllTextAsync(scenePath, sceneJson);
            await File.WriteAllBytesAsync(imageExportSmokeOutputPath, []);

            var document = await session.DocumentService.OpenPathAsync(scenePath);
            AttachDocumentToSession(session, document, select: true);
            var loadDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
            while (session.PendingDocumentLoad is not null &&
                DateTimeOffset.UtcNow < loadDeadline)
            {
                await Task.Delay(50);
            }
            if (session.PendingDocumentLoad is not null ||
                session.CoreWebView is not { } coreWebView)
            {
                throw new InvalidOperationException(
                    "The representative export scene did not load.");
            }

            var destination = await StorageFile.GetFileFromPathAsync(
                imageExportSmokeOutputPath);
            StartImageExport(session, coreWebView, destination);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Title = $"Excalidraw Desktop — Image export smoke failed: {exception.Message}";
        }
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

    private void LogAction(
        string action,
        string inputSource,
        DocumentSession? targetSession = null)
    {
        string? windowId = null;
        try
        {
            windowId = workspaceCoordinator.GetLogicalWindowId(this);
        }
        catch (InvalidOperationException)
        {
        }

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
        LogAction("tab.new", "tab_button");
        CreateTab();
    }

    private void OnFileNewTabClick(object sender, RoutedEventArgs args)
    {
        LogAction("tab.new", "menu");
        CreateTab();
    }

    private void OnFileNewWindowClick(object sender, RoutedEventArgs args)
    {
        LogAction("window.new", "menu");
        workspaceCoordinator.CreateWindow();
    }

    private void OnFileOpenClick(object sender, RoutedEventArgs args)
    {
        LogAction("document.open", "menu");
        if (ActiveSession is { } session)
        {
            _ = RequestOpenDocumentAsync(session);
        }
    }

    private void OnFileSaveClick(object sender, RoutedEventArgs args)
    {
        LogAction("document.save", "menu");
        RequestSaveFromFileMenu(saveAs: false);
    }

    private void OnFileSaveAsClick(object sender, RoutedEventArgs args)
    {
        LogAction("document.save_as", "menu");
        RequestSaveFromFileMenu(saveAs: true);
    }

    private async void OnFileExportPngClick(object sender, RoutedEventArgs args)
    {
        LogAction("document.export_png", "menu");
        await ExportActiveSessionAsPngAsync();
    }

    private async Task ExportActiveSessionAsPngAsync()
    {
        if (openPickerActive ||
            ActiveSession is not
            {
                IsReady: true,
                IsExporting: false,
                IsResuming: false,
                IsRestoringFromHibernation: false,
                CoreWebView: { } coreWebView,
            } session)
        {
            return;
        }

        openPickerActive = true;
        UpdateFileMenuState(session);
        try
        {
            var destination = await imageExportService.PickDestinationAsync(
                this,
                session.DisplayName);
            if (destination is null ||
                !sessions.Contains(session) ||
                !ReferenceEquals(session.CoreWebView, coreWebView))
            {
                return;
            }

            StartImageExport(session, coreWebView, destination);
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error(
                "image_export.start_failed",
                exception,
                new { sessionId = session.RecoveryId });
            Debug.WriteLine($"Could not start PNG export: {exception}");
            if (session.PendingImageExport is { } pending)
            {
                await FailImageExportAsync(
                    session,
                    pending.ExportId,
                    DesktopResources.Get(
                        "PngExportStartFailed",
                        "The PNG export could not be started."));
            }
            else
            {
                await ShowImageExportErrorAsync(
                    DesktopResources.Get(
                        "PngExportStartFailed",
                        "The PNG export could not be started."));
            }
        }
        finally
        {
            openPickerActive = false;
            UpdateFileMenuState(ActiveSession);
        }
    }

    private void StartImageExport(
        DocumentSession session,
        CoreWebView2 coreWebView,
        StorageFile destination)
    {
        var exportId = Guid.NewGuid();
        session.PendingImageExport = new PendingImageExport(
            exportId,
            destination,
            new CancellationTokenSource());
        session.ExportStatusMessage = null;
        UpdateFileMenuState(session);
        UpdateStatusBar(session);
        coreWebView.PostWebMessageAsJson(
            BridgeEventJson.Create(
                "image.exportRequested",
                new
                {
                    exportId = exportId.ToString("D"),
                    uploadUrl = $"{ImageExportPolicy.GetUploadOrigin(session.TabOrigin)}" +
                        $"/_desktop/export/{exportId:D}",
                    maxDimension = ImageExportPolicy.MaxDimension,
                    maxBytes = ImageExportPolicy.MaxPngBytes,
                    scale = ImageExportPolicy.Scale,
                    padding = ImageExportPolicy.Padding,
                }));
        _ = WatchImageExportTimeoutAsync(
            session,
            exportId,
            session.PendingImageExport.Cancellation.Token);
    }

    private void CompleteImageExport(
        DocumentSession session,
        PendingImageExport pending,
        string fileName)
    {
        if (session.PendingImageExport?.ExportId != pending.ExportId)
        {
            return;
        }

        pending.Cancellation.Cancel();
        pending.Cancellation.Dispose();
        session.PendingImageExport = null;
        session.ExportStatusMessage = DesktopResources.Format(
            "ExportedFileFormat",
            "Exported {0}",
            fileName);
        UpdateFileMenuState(ActiveSession);
        UpdateStatusBar(session);
#if DEBUG
        if (runImageExportSmoke &&
            imageExportSmokeOutputPath is { } outputPath &&
            DesktopDocumentPath.Equals(pending.Destination.Path, outputPath))
        {
            Title = "Excalidraw Desktop — Image export smoke passed";
        }
#endif
        _ = ClearImageExportStatusAsync(session, session.ExportStatusMessage);
    }

    private async Task WatchImageExportTimeoutAsync(
        DocumentSession session,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (sessions.Contains(session) &&
            session.PendingImageExport?.ExportId == exportId)
        {
            await FailImageExportAsync(
                session,
                exportId,
                DesktopResources.Get(
                    "PngExportTimeout",
                    "The PNG export took too long and was cancelled."));
        }
    }

    private async Task ClearImageExportStatusAsync(
        DocumentSession session,
        string expectedMessage)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (sessions.Contains(session) &&
            string.Equals(
                session.ExportStatusMessage,
                expectedMessage,
                StringComparison.Ordinal))
        {
            session.ExportStatusMessage = null;
            UpdateStatusBar(session);
        }
    }

    private async Task FailImageExportAsync(
        DocumentSession session,
        Guid exportId,
        string message)
    {
        if (session.PendingImageExport is not { } pending ||
            pending.ExportId != exportId)
        {
            return;
        }

        pending.Cancellation.Cancel();
        pending.Cancellation.Dispose();
        session.PendingImageExport = null;
        session.ExportStatusMessage = null;
        UpdateFileMenuState(ActiveSession);
        UpdateStatusBar(session);
#if DEBUG
        if (runImageExportSmoke)
        {
            Title = $"Excalidraw Desktop — Image export smoke failed: {message}";
            return;
        }
#endif
        await ShowImageExportErrorAsync(message);
    }

    private async Task ShowImageExportErrorAsync(string message)
    {
        if (resourcesDisposed || DocumentTabs.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = DocumentTabs.XamlRoot,
            Title = DesktopResources.Get(
                "PngExportErrorTitle",
                "Could not export PNG"),
            Content = message,
            CloseButtonText = DesktopResources.Get("OkButton", "OK"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private static string GetImageExportFailureMessage(Exception exception) =>
        exception switch
        {
            BridgeProtocolException => exception.Message,
            UnauthorizedAccessException =>
                DesktopResources.Get(
                    "PngExportAccessDenied",
                    "Excalidraw Desktop does not have permission to write the selected location."),
            IOException =>
                DesktopResources.Get(
                    "PngExportWriteFailed",
                    "The PNG could not be written. Check the destination and available disk space."),
            _ => DesktopResources.Get(
                "PngExportDestinationFailed",
                "The PNG could not be written to the selected location."),
        };

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
        LogAction("document.save_all", "menu");
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
        LogAction("tab.close", "menu");
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

    private void OnFileCloseWindowClick(object sender, RoutedEventArgs args)
    {
        LogAction("window.close", "menu");
        Close();
    }

    private void OnFileExitClick(object sender, RoutedEventArgs args)
    {
        LogAction("application.exit", "menu");
        _ = workspaceCoordinator.RequestExitAsync();
    }

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
            if (!await ResolveWindowCloseAsync())
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
        LogAction("tab.new", "keyboard");
        CreateTab();
    }

    private void OnNewWindowAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        LogAction("window.new", "keyboard");
        workspaceCoordinator.CreateWindow();
    }

    private void OnCloseTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        LogAction("tab.close", "keyboard");
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

    private void OnOpenAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        LogAction("document.open", "keyboard");
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
        LogAction("document.save", "keyboard");
        RequestSaveFromFileMenu(saveAs: false);
    }

    private void OnSaveAsAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        LogAction("document.save_as", "keyboard");
        RequestSaveFromFileMenu(saveAs: true);
    }

    private void OnCloseWindowAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        LogAction("window.close", "keyboard");
        Close();
    }

    private void OnSaveAllAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        LogAction("document.save_all", "keyboard");
        _ = SaveAllFromFileMenuAsync();
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
        _ = RequestCloseSessionAndReportAsync(session);
    }

    private async Task<bool> RequestCloseSessionAndReportAsync(
        DocumentSession session)
    {
        try
        {
            return await RequestCloseSessionAsync(session);
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
        pendingTearOutWindow = workspaceCoordinator.CreateWindow(
            activate: false,
            createInitialTab: false);
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
        // Removing and disposing a TabViewItem while WinUI is routing the
        // pointer/click event that targeted it can invalidate the input tree.
        // Keep this deferral inside the shared close path so bulk and future
        // callers receive the same protection.
        if (!await YieldToDispatcherAsync())
        {
            return false;
        }

        if (!sessions.Contains(session) || session.ClosePromptOpen)
        {
            return false;
        }

        if (session.IsExporting)
        {
            await ShowImageExportErrorAsync(
                DesktopResources.Get(
                    "WaitForDrawingExport",
                    "Wait for the current PNG export to finish before closing this drawing."));
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
            var decision = desktopPreferences.SaveDirtyDrawingsOnClose
                ? CloseDecision.Save
                : await session.DocumentService.PromptToSaveBeforeCloseAsync();
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
        DetachWebView(session);
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
            ConfigureWebView(session, coreWebView);
        }
        if (session.DocumentService.DocumentPath is { } path)
        {
            WatchExternalFile(session, path);
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
        sessions.Contains(session) &&
        session.IsReady &&
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
                openPickerActive,
            session.IsSuspensionChanging,
            session.IsResuming,
            session.IsUnloading,
            session.IsExporting));

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
        var newTab = new MenuFlyoutItem
        {
            Text = DesktopResources.Get("ContextNewTab", "New Tab"),
        };
        newTab.Click += (_, _) => CreateTab();

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

        args.Cancel = true;
        if (windowClosePromptOpen)
        {
            return;
        }

        windowClosePromptOpen = true;
        try
        {
            if (await ResolveWindowCloseAsync())
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
        }
    }

    private async Task<bool> ResolveWindowCloseAsync()
    {
        if (sessions.Any(session => session.IsExporting))
        {
            await ShowImageExportErrorAsync(
                DesktopResources.Get(
                    "WaitForWindowExports",
                    "Wait for PNG exports to finish before closing this window."));
            return false;
        }

        var dirtySessions = sessions.Where(session => session.IsDirty).ToList();
        if (dirtySessions.Count == 0)
        {
            await PersistWorkspaceAsync();
            await workspaceCoordinator.PruneRecoverySnapshotsAsync();
            return true;
        }

        if (desktopPreferences.SaveDirtyDrawingsOnClose)
        {
            if (!await SaveAllForWindowCloseAsync(dirtySessions))
            {
                return false;
            }
            await PersistWorkspaceAsync();
            await workspaceCoordinator.PruneRecoverySnapshotsAsync();
            return true;
        }

        var names = string.Join(
            Environment.NewLine,
            dirtySessions.Select(session => $"• {session.DisplayName}"));
        var reviewRequested = false;
        var reviewButton = new Button
        {
            Content = DesktopResources.Get(
                "ReviewTabsButton",
                "Review tabs individually"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(reviewButton, "ReviewTabsButton");
        AutomationProperties.SetName(
            reviewButton,
            DesktopResources.Get(
                "ReviewTabsAutomationName",
                "Review unsaved tabs individually"));
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new TextBlock
        {
            Text = DesktopResources.Format(
                "UnsavedDrawingsCountFormat",
                "{0} drawing(s) have unsaved changes:\n\n{1}",
                dirtySessions.Count,
                names),
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(reviewButton);
        var dialog = new ContentDialog
        {
            XamlRoot = DocumentTabs.XamlRoot,
            Title = DesktopResources.Get("UnsavedDrawingsTitle", "Unsaved drawings"),
            Content = content,
            PrimaryButtonText = DesktopResources.Get("SaveAllButton", "Save all"),
            SecondaryButtonText = DesktopResources.Get(
                "DiscardAllButton",
                "Discard all"),
            CloseButtonText = DesktopResources.Get("CancelButton", "Cancel"),
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
            return false;
        }
        if (result == ContentDialogResult.Primary)
        {
            if (!await SaveAllForWindowCloseAsync(dirtySessions))
            {
                return false;
            }
            await PersistWorkspaceAsync();
            await workspaceCoordinator.PruneRecoverySnapshotsAsync();
            return true;
        }
        if (result != ContentDialogResult.Secondary)
        {
            return false;
        }

        var discardedSessions = dirtySessions
            .Where(session => sessions.Contains(session) && session.IsDirty)
            .ToArray();
        foreach (var discardedSession in discardedSessions)
        {
#if DEBUG
            discardedSession.ForceDirtyForSmoke = false;
#endif
            discardedSession.IsDirty = false;
        }
        workspaceCoordinator.RecordWindowDiscarded(this);
        try
        {
            await workspaceCoordinator.PersistWorkspaceAsync();
            await workspaceCoordinator.PruneRecoverySnapshotsAsync();
            return true;
        }
        catch
        {
            foreach (var discardedSession in discardedSessions)
            {
                discardedSession.IsDirty = true;
                UpdateTabHeader(discardedSession);
            }
            throw;
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
            IsResuming: false,
            IsRestoringFromHibernation: false,
            CoreWebView: not null,
        };
        SaveMenuItem.IsEnabled = canUseEditor;
        SaveAsMenuItem.IsEnabled = canUseEditor;
        ExportPngMenuItem.IsEnabled = canUseEditor && active?.IsExporting != true;
        SaveAllMenuItem.IsEnabled = !settingsPageVisible && sessions.Any(session =>
            session.IsDirty &&
            session.IsReady &&
            !session.IsResuming &&
            session.CoreWebView is not null);
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
        else if (session.IsResuming)
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
        if (!settingsPageVisible &&
            ActiveSession is { ExternalFileState: not ExternalFileState.None } session)
        {
            await ShowExternalFileConflictAsync(session);
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

        await Launcher.LaunchUriAsync(uri);
    }

}
