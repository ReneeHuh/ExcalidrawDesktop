using System.Diagnostics;
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace ExcalidrawDesktop.App.Services;

/// <summary>
/// Owns editor lifecycle transitions and WebView event subscriptions for one
/// window. The host supplies session selection and reacts to UI notifications.
/// </summary>
internal sealed class EditorSessionController
{
    private static readonly object WebViewEnvironmentLock = new();
    private static Task<CoreWebView2Environment>? webViewEnvironmentTask;
    private readonly IReadOnlyList<DocumentSession> sessions;
    private readonly Func<DocumentSession?> getActiveSession;
    private readonly string webAssetPath;
    private readonly Func<string, Task> openExternalUri;
    private readonly bool enableSmokeApi;
    private readonly RecoverySnapshotStore recoverySnapshots;

    public EditorSessionController(
        IReadOnlyList<DocumentSession> sessions,
        Func<DocumentSession?> getActiveSession,
        string webAssetPath,
        Func<string, Task> openExternalUri,
        bool enableSmokeApi,
        RecoverySnapshotStore recoverySnapshots)
    {
        this.sessions = sessions;
        this.getActiveSession = getActiveSession;
        this.webAssetPath = webAssetPath;
        this.openExternalUri = openExternalUri;
        this.enableSmokeApi = enableSmokeApi;
        this.recoverySnapshots = recoverySnapshots;
    }

    public event Action<DocumentSession>? StateChanged;
    public event Action<DocumentSession>? Ready;
    public event Action<DocumentSession>? Resumed;
    public event Action<DocumentSession>? Failed;
    public event Action<DocumentSession, string>? TitleChanged;
    public event Action<DocumentSession, bool>? CloseBarrierReady;
    public event Action<DocumentSession, Guid, string, bool>? DocumentLoadApplied;
    public Func<bool>? CommandsBlocked { get; set; }
    public Func<BridgeMessage, Task<object?>>? LibraryRequest { get; set; }
    public event Action<DocumentSession, WebView2>? EditorRecreated;
    public event Action<DocumentSession, CoreWebView2,
        CoreWebView2WebResourceRequestedEventArgs>? ImageExportRequested;

#if DEBUG
    public Func<Task>? BeforeInitializeForSmoke { get; set; }
#endif

    public void MarkReady(DocumentSession session)
    {
        if (!sessions.Contains(session))
        {
            return;
        }

        session.IsReady = true;
        session.LastLifecycleFailure = null;
        if (session.IsRetrying && session.PendingDocumentLoad is not null)
        {
            _ = WatchRetryLoadAsync(session, session.CoreWebView);
        }
        else
        {
            session.IsRetrying = false;
            session.Content.ShowReady();
        }
        Ready?.Invoke(session);
    }

    public static bool CanPostMessage(DocumentSession session) =>
        session.IsReady &&
        !session.IsSuspended &&
        !session.IsSuspensionChanging &&
        !session.IsUnloading &&
        !session.IsUnloaded &&
        !session.IsResuming &&
        session.CoreWebView is not null;

    /// <summary>
    /// Whether a save can be requested from the editor right now: the page has
    /// signalled ready and is not sleeping or mid transition. Dirty tabs that
    /// were restored from recovery but never selected are not ready.
    /// </summary>
    public static bool CanRequestSave(DocumentSession session) =>
        session.IsReady &&
        session.CoreWebView is not null &&
        !session.IsRetrying &&
        session.PendingDocumentLoad is null &&
        !session.IsSuspended &&
        !session.IsSuspensionChanging &&
        !session.IsUnloaded &&
        !session.IsUnloading &&
        !session.IsResuming &&
        !session.IsRestoringFromHibernation;

    public bool CanSuspend(DocumentSession session) =>
        sessions.Contains(session) &&
        session.CloseBarrierId is null &&
        !ReferenceEquals(session, getActiveSession()) &&
        session.IsReady &&
        !session.IsDirty &&
        !session.HasUnsavedLibrary &&
        !session.IsInitializing &&
        !session.IsRetrying &&
        !session.IsExporting &&
        !session.IsBridgeDispatching &&
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

    public bool CanUnload(DocumentSession session) =>
        sessions.Contains(session) &&
        session.CloseBarrierId is null &&
        !ReferenceEquals(session, getActiveSession()) &&
        session.IsReady &&
        !session.IsDirty &&
        !session.HasUnsavedLibrary &&
        !session.IsInitializing &&
        !session.IsRetrying &&
        !session.IsExporting &&
        !session.IsBridgeDispatching &&
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

    public async Task<bool> UnloadAsync(
        DocumentSession session,
        bool requireIdle)
    {
        if (!CanUnload(session) ||
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
            StateChanged?.Invoke(session);
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
        session.CloseBarrierId is null &&
        !ReferenceEquals(session, getActiveSession()) &&
        session.IsReady &&
        !session.IsDirty &&
        !session.HasUnsavedLibrary &&
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

    public async Task WakeAsync(DocumentSession session)
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
        EditorRecreated?.Invoke(session, editor);

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

        StateChanged?.Invoke(session);
        await InitializeAsync(session);
    }

    public async Task<bool> SuspendAsync(
        DocumentSession session,
        bool requireIdle)
    {
        if (!CanSuspend(session) ||
            (requireIdle && session.InactiveSince >
                DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5)))
        {
            return false;
        }

        session.IsSuspensionChanging = true;
        try
        {
            StateChanged?.Invoke(session);
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

            if (ReferenceEquals(session, getActiveSession()) ||
                session.Content.Visibility == Visibility.Visible)
            {
                if (session.CoreWebView.IsSuspended)
                {
                    session.CoreWebView.Resume();
                }
                return false;
            }

            session.IsSuspended = true;
            StateChanged?.Invoke(session);
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
        session.CloseBarrierId is null &&
        !ReferenceEquals(session, getActiveSession()) &&
        session.Content.Visibility != Visibility.Visible &&
        session.IsReady &&
        !session.IsDirty &&
        !session.HasUnsavedLibrary &&
        !session.IsExporting &&
        !session.IsUnloading &&
        session.CoreWebView is not null &&
        session.ExternalFileState is ExternalFileState.None &&
        session.PendingDocumentLoad is null;

    public void Resume(DocumentSession session)
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
            StateChanged?.Invoke(session);
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
        Resumed?.Invoke(session);
        StateChanged?.Invoke(session);
    }

    // Activation may initialize a new tab, but only explicit Retry may restart
    // a failed one after selecting safe replacement content.
    public Task InitializeAsync(DocumentSession session) =>
        session.IsRetrying || session.LastLifecycleFailure is not null
            ? Task.CompletedTask
            : InitializeCoreAsync(session);

    private async Task InitializeCoreAsync(DocumentSession session)
    {
        if (!sessions.Contains(session) ||
            session.IsReady ||
            session.IsInitializing)
        {
            return;
        }

        session.IsInitializing = true;
        try
        {
#if DEBUG
            if (BeforeInitializeForSmoke is { } beforeInitialize)
            {
                await beforeInitialize();
            }
#endif
            if (!sessions.Contains(session))
            {
                return;
            }
            var webView = session.Content.Editor;
            TitleChanged?.Invoke(session, DesktopResources.Get(
                "InitializingEditorTitle",
                "Excalidraw Desktop — Initializing editor…"));
            var entryPointPath = Path.Combine(webAssetPath, "index.html");
            if (!File.Exists(entryPointPath))
            {
                throw new FileNotFoundException(
                    "The desktop web bundle is missing. Run tools/Build-Desktop.ps1 first.",
                    entryPointPath);
            }

            var webViewEnvironment = GetWebViewEnvironment();
            await webView.EnsureCoreWebView2Async(await webViewEnvironment);
            if (!sessions.Contains(session) ||
                !session.Content.HasEditor ||
                !ReferenceEquals(session.Content.Editor, webView))
            {
                return;
            }
            AttachWebView(session, webView.CoreWebView2);
            var entryPoint = enableSmokeApi
                ? $"{session.TabOrigin.EntryPoint}?desktopSmoke=1"
                : session.TabOrigin.EntryPoint;
            session.NavigationPolicy = new EditorNavigationPolicy(entryPoint);
            session.EditorNavigationId = null;
            webView.CoreWebView2.Navigate(entryPoint);
        }
        catch (Exception exception)
        {
            if (!sessions.Contains(session))
            {
                return;
            }
            session.IsRetrying = false;
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
            TitleChanged?.Invoke(session, DesktopResources.Get(
                "EditorStartupFailedTitle",
                "Excalidraw Desktop — Editor startup failed"));
            Debug.WriteLine(exception);
        }
        finally
        {
            session.IsInitializing = false;
        }
    }

    private static Task<CoreWebView2Environment> GetWebViewEnvironment()
    {
        lock (WebViewEnvironmentLock)
        {
            if (webViewEnvironmentTask is { IsCompleted: true, IsCompletedSuccessfully: false })
            {
                // A failed attempt (runtime missing, data folder locked) must
                // not be cached: the failure UI tells the user to fix the
                // cause and choose Retry, which needs a fresh creation.
                webViewEnvironmentTask = null;
            }
            return webViewEnvironmentTask ??=
                CreateWebViewEnvironmentAsync();
        }
    }

    private static async Task<CoreWebView2Environment>
        CreateWebViewEnvironmentAsync()
    {
        return await CoreWebView2Environment.CreateWithOptionsAsync(
            null,
            DesktopPaths.WebView2DataDirectory,
            new CoreWebView2EnvironmentOptions());
    }

    public void AttachWebView(DocumentSession session, CoreWebView2 coreWebView)
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

        coreWebView.Settings.AreBrowserAcceleratorKeysEnabled = false;
        coreWebView.Settings.AreDefaultContextMenusEnabled = false;
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
            (_, args) => ImageExportRequested?.Invoke(session, coreWebView, args);

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

    public static void DetachWebView(DocumentSession session)
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

    public async Task RetryAsync(DocumentSession session)
    {
        if (session.IsBridgeDispatching)
        {
            // A synchronous editor event can request Retry. Let its handler
            // return before replacing the page; longer operations still block it.
            await Task.Yield();
        }
        if (!sessions.Contains(session) || session.IsInitializing ||
            session.IsRetrying || session.IsBridgeDispatching || session.IsMoving)
        {
            return;
        }

        session.IsRetrying = true;
        session.IsReady = false;
        session.IsSuspended = false;
        session.IsUnloaded = false;
        session.LastLifecycleFailure = null;
        StateChanged?.Invoke(session);
        try
        {
            // Stop the old page from changing dirty state while we read its
            // replacement content, but keep the failure view until it is valid.
            session.DetachEditorHandlers?.Invoke();
            session.DetachEditorHandlers = null;
            DetachWebView(session);
            var load = await EditorRetryContent.LoadAsync(
                new EditorRetryState(
                    session.DisplayName,
                    session.IsDirty,
                    session.DocumentService.HasActiveFile,
                    session.PendingDocumentLoad,
                    session.HibernatedContent),
                () => LoadRecoveryForRetryAsync(session),
                async () =>
                {
                    var document = await session.DocumentService.ReloadActiveAsync();
                    session.DocumentService.StageOpen(document);
                    return new PendingEditorLoad(document.FileName, document.Content, false);
                });
            if (!sessions.Contains(session))
            {
                session.IsRetrying = false;
                return;
            }
            if (load is not null)
            {
                ExcalidrawDocumentValidator.ValidateForSave(
                    load.IsRecovery ? RecoveryFileBaseline.GetDocumentContent(load.Content) : load.Content,
                    DocumentService.MaxDocumentBytes);
                if (!load.IsRecovery && session.DocumentService.PendingOpenId is null &&
                    !session.DocumentService.StageActiveFileReload())
                {
                    throw new InvalidOperationException("The drawing has no file to reload.");
                }
            }
            session.PendingDocumentLoad = load is null ? null : load with
            {
                LoadId = session.DocumentService.PendingOpenId ?? Guid.NewGuid(),
            };
            var editor = session.Content.RecreateEditor(DesktopResources.Get(
                "RetryingEditor",
                "Retrying editor…"));
            EditorRecreated?.Invoke(session, editor);
            StateChanged?.Invoke(session);
            await InitializeCoreAsync(session);
        }
        catch (Exception exception)
        {
            if (sessions.Contains(session))
            {
                ShowRetryFailure(session, exception.Message);
            }
            else
            {
                session.IsRetrying = false;
            }
            DiagnosticLogService.Error("editor.retry_failed", exception);
        }
    }

    private async Task<string?> LoadRecoveryForRetryAsync(DocumentSession session)
    {
        await session.RecoveryGate.WaitAsync();
        try
        {
            return await recoverySnapshots.LoadAsync(session.RecoveryId);
        }
        finally
        {
            session.RecoveryGate.Release();
        }
    }

    public void CompleteRetry(DocumentSession session)
    {
        if (!sessions.Contains(session) || !session.IsRetrying ||
            !session.IsReady || session.PendingDocumentLoad is not null)
        {
            return;
        }
        session.IsRetrying = false;
        session.Content.ShowReady();
        StateChanged?.Invoke(session);
    }

    public void HandleLoadFailure(DocumentSession session)
    {
        if (sessions.Contains(session) && session.PendingDocumentLoad is not null)
        {
            ShowRetryFailure(session, "The editor could not load the drawing content.");
        }
    }

    private async Task WatchRetryLoadAsync(DocumentSession session, CoreWebView2? webView)
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        if (sessions.Contains(session) && session.IsRetrying &&
            ReferenceEquals(session.CoreWebView, webView))
        {
            ShowRetryFailure(session, "The drawing restore timed out.");
        }
    }

    private void ShowRetryFailure(DocumentSession session, string failure)
    {
        session.IsReady = false;
        session.IsRetrying = false;
        // A delayed ready/dirty event from a failed restore must not make the
        // drawing editable or delete its recovery snapshot.
        DetachWebView(session);
        session.LastLifecycleFailure = failure;
        session.Content.ShowFailure(
            DesktopResources.Get("EditorRetryFailed", "Could not restore drawing"),
            session.IsDirty || session.PendingDocumentLoad?.IsRecovery == true
                ? DesktopResources.Get(
                    "EditorRetryRecoveryGuidance",
                    "The recovery data for this unsaved drawing is missing or unreadable. No drawing file has been changed.")
                : DesktopResources.Get(
                    "EditorRetryFileGuidance",
                    "Retry again or reopen the drawing file. No drawing file has been changed."));
        StateChanged?.Invoke(session);
    }

    private async void OnNavigationStarting(
        DocumentSession session,
        CoreWebView2NavigationStartingEventArgs args)
    {
        if (session.NavigationPolicy?.TryBeginNavigation(args.Uri) == true)
        {
            session.EditorNavigationId = args.NavigationId;
            session.IsReady = false;
            return;
        }

        args.Cancel = true;
        if (!session.TabOrigin.Matches(args.Uri) && args.Uri != "about:blank")
        {
            await TryOpenExternalUriAsync(args.Uri);
        }
    }

    private async void OnNavigationCompleted(
        DocumentSession session,
        CoreWebView2 coreWebView,
        CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!sessions.Contains(session) ||
            !ReferenceEquals(session.CoreWebView, coreWebView) ||
            session.EditorNavigationId != args.NavigationId)
        {
            return;
        }

        if (!args.IsSuccess)
        {
            session.IsReady = false;
            session.IsRetrying = false;
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
            TitleChanged?.Invoke(session, DesktopResources.Get(
                "EditorStartupFailedTitle",
                "Excalidraw Desktop — Editor startup failed"));
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(5));
        if (session.IsReady ||
            session.LastLifecycleFailure is not null ||
            !sessions.Contains(session) ||
            !ReferenceEquals(session.CoreWebView, coreWebView))
        {
            return;
        }

#if DEBUG
        string diagnostics;
        try
        {
            diagnostics = await coreWebView.ExecuteScriptAsync(
                "JSON.stringify({readyState:document.readyState,rootChildren:document.getElementById('root')?.childElementCount??-1,origin:location.origin,transport:!!window.chrome?.webview})");
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("editor.startup_diagnostics_failed", exception);
            return;
        }
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
        session.IsRetrying = false;
        TitleChanged?.Invoke(session, DesktopResources.Get(
            "EditorStartupFailedTitle",
            "Excalidraw Desktop — Editor startup failed"));
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

        HandleProcessFailure(session, args.ProcessFailedKind.ToString());
    }

    public void HandleProcessFailure(
        DocumentSession session,
        string failureKind)
    {
        session.IsReady = false;
        session.IsRetrying = false;
        session.IsSuspended = false;
        session.LastLifecycleFailure = failureKind;
        Failed?.Invoke(session);
        session.Content.ShowFailure(
            DesktopResources.Get(
                "EditorProcessStopped",
                "The editor process stopped unexpectedly."),
            DesktopResources.Format(
                "EditorProcessFailureGuidanceFormat",
                "WebView2 reported {0}. Retry the editor. If failures continue, repair the WebView2 Runtime.",
                failureKind),
            showWebView2Help: true);
        StateChanged?.Invoke(session);
        TitleChanged?.Invoke(session, DesktopResources.Get(
            "EditorProcessFailedTitle",
            "Excalidraw Desktop — Editor process failed"));
    }

    private async void OnNewWindowRequested(
        CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        await TryOpenExternalUriAsync(args.Uri);
    }

    private async Task TryOpenExternalUriAsync(string uri)
    {
        try
        {
            await openExternalUri(uri);
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("editor.external_uri_failed", exception);
        }
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
            if (message.Kind == "event" && message.Method == "library.stateChanged")
            {
                if (message.Payload is { ValueKind: System.Text.Json.JsonValueKind.Object } libraryPayload &&
                    libraryPayload.TryGetProperty("hasUnsavedChanges", out var unsaved) &&
                    unsaved.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                    session.HasUnsavedLibrary = unsaved.GetBoolean();
                return;
            }
            if (message.Kind == "request" && message.Method is "library.load" or "library.save" &&
                LibraryRequest is { } libraryRequest)
            {
                try
                {
                    var response = await libraryRequest(message);
                    session.TryPostEditorMessage(BridgeResponseJson.Success(message, response));
                }
                catch (BridgeProtocolException exception)
                {
                    session.TryPostEditorMessage(BridgeResponseJson.Error(message, exception.Code, exception.Message));
                }
                catch (Exception exception)
                {
                    DiagnosticLogService.Error("library.operation_failed", exception);
                    session.TryPostEditorMessage(BridgeResponseJson.Error(message, "LibraryUnavailable",
                        "The shared library could not be read or saved."));
                }
                return;
            }
            if (message.Kind == "event" && message.Method is "document.loadApplied" or "document.loadFailed" or "document.loadCancelled")
            {
                if (message.Payload is not { ValueKind: System.Text.Json.JsonValueKind.Object } payload ||
                    !payload.TryGetProperty("loadId", out var id) ||
                    id.ValueKind != System.Text.Json.JsonValueKind.String ||
                    !Guid.TryParseExact(id.GetString(), "D", out var loadId) ||
                    session.PendingDocumentLoad?.LoadId != loadId)
                    return;
                if (message.Method != "document.loadApplied")
                {
                    HandleLoadFailure(session);
                    return;
                }
                if (payload.TryGetProperty("fileName", out var fileName) &&
                    fileName.ValueKind == System.Text.Json.JsonValueKind.String &&
                    payload.TryGetProperty("isRecovery", out var recovery) &&
                    recovery.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                    DocumentLoadApplied?.Invoke(session, loadId, fileName.GetString()!, recovery.GetBoolean());
                return;
            }
            if (message.Kind == "event" && message.Method == "document.closeBarrierReady")
            {
                if (message.Payload is { } payload &&
                    payload.TryGetProperty("barrierId", out var id) &&
                    Guid.TryParse(id.GetString(), out var barrierId) &&
                    session.CloseBarrierId == barrierId &&
                    payload.TryGetProperty("isDirty", out var dirty) &&
                    dirty.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                {
                    CloseBarrierReady?.Invoke(session, dirty.GetBoolean());
                    session.CloseBarrierCompletion?.TrySetResult(
                        !payload.TryGetProperty("canClose", out var canClose) ||
                        canClose.ValueKind != System.Text.Json.JsonValueKind.False);
                }
                return;
            }
            if (message.Kind == "request" && CommandsBlocked?.Invoke() == true &&
                message.Method is "document.new" or "document.open" or "document.save" or "document.saveAs")
            {
                var isCloseSave = message.Payload is { ValueKind: System.Text.Json.JsonValueKind.Object } payload &&
                    payload.TryGetProperty("closeRequestId", out var id) &&
                    id.ValueKind == System.Text.Json.JsonValueKind.String &&
                    Guid.TryParse(id.GetString(), out var requestId) && session.CloseRequestId == requestId;
                if (!isCloseSave)
                {
                    session.TryPostEditorMessage(BridgeResponseJson.Error(message,
                        "DocumentClosing", "Finish or cancel closing before changing the drawing."));
                    return;
                }
            }
            if (message.Kind == "request" &&
                message.Method is "document.save" or "document.saveAs" &&
                !CanRequestSave(session))
            {
                session.TryPostEditorMessage(BridgeResponseJson.Error(
                    message,
                    "EditorNotReady",
                    "Wait for the drawing to finish loading before saving."));
                return;
            }
            await session.Dispatcher.DispatchAsync(coreWebView, message);
        }
        catch (BridgeProtocolException exception)
        {
            Debug.WriteLine($"Rejected bridge message ({exception.Code}): {exception.Message}");
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("editor.bridge_dispatch_failed", exception);
            session.LastLifecycleFailure = exception.Message;
            StateChanged?.Invoke(session);
        }
        finally
        {
            session.BridgeDispatchDepth--;
        }
    }

    public void CompleteHibernationRestore(DocumentSession session)
    {
        session.IsRestoringFromHibernation = false;
        session.IsResuming = false;
        session.HibernatedContent = null;
        session.InactiveSince = null;
        StateChanged?.Invoke(session);
    }
}
