using System.Diagnostics;
using System.Text.Json;
using ExcalidrawDesktop.Core;
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.System;

namespace ExcalidrawDesktop.App;

// Smoke-test scenarios, fixtures, and their per-window state.
public sealed partial class MainWindow
{
#if DEBUG
    private readonly Stopwatch performanceStartup = Stopwatch.StartNew();
    private bool tabSmokeStarted;
    private bool titleBarSmokeStarted;
    private bool multiWindowSmokeStarted;
    private bool multiWindowExitSmokeStarted;
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
    private bool recoverySmokeStarted;
    private int recoverySnapshotsSaved;
    private int recoveryTabsRestored;
    private TaskCompletionSource? multiWindowInitializationRelease;

    private async Task OnRecoverySnapshotSavedForSmokeAsync(DocumentSession session)
    {
        if (smoke.RunRecoverySmoke)
        {
            recoverySnapshotsSaved++;
            if (recoverySnapshotsSaved == sessions.Count)
            {
                await PersistWorkspaceAsync();
                Title = "Excalidraw Desktop — Recovery snapshot saved";
            }
        }
    }
#endif

    private void OnEditorLanguageApplied(
        DocumentSession session,
        string langCode,
        string direction)
    {
#if DEBUG
        if (!smoke.RunLocalizationSmoke ||
            localizationSmokeComplete ||
            !session.IsReady ||
            smoke.LocalizationSmokeLanguage is null)
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
                smoke.LocalizationSmokeLanguage,
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
        _ = editorSessions.RetryAsync(session);
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
                RequestedLanguage = smoke.LocalizationSmokeLanguage,
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
            ? $"Excalidraw Desktop — Localization smoke passed: {smoke.LocalizationSmokeLanguage}"
            : $"Excalidraw Desktop — Localization smoke failed: {smoke.LocalizationSmokeLanguage}";
    }
#endif

#if DEBUG
    private void SimulateWebViewProcessFailureForSmoke(DocumentSession session) =>
        editorSessions.HandleProcessFailure(session, "SmokeProcessFailure");
#endif

    private void TryRunTabSmoke()
    {
#if DEBUG
        if (!smoke.RunTabSmoke || tabSmokeStarted)
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
        if (!smoke.RunMultiWindowSmoke || multiWindowSmokeStarted)
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
        if ((!smoke.RunMultiWindowExitSmoke && !smoke.RunMultiWindowDirtyExitSmoke) ||
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

            if (smoke.RunMultiWindowDirtyExitSmoke)
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
            if (!await editorSessions.SuspendAsync(session, requireIdle: true) ||
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

            editorSessions.Resume(session);
            if (!session.IsResuming || session.IsSuspended ||
                workspaceCoordinator.MoveSessionToNewWindow(this, session) ||
                !sessions.Contains(session))
            {
                throw new InvalidOperationException(
                    "A resuming drawing was allowed to enter a transfer.");
            }
            if (!await AsyncWait.UntilAsync(
                    () => !session.IsResuming,
                    TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException(
                    "The transfer fixture did not finish resuming.");
            }

            session.InactiveSince = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(16);
            if (!await editorSessions.UnloadAsync(session, requireIdle: true) ||
                !session.IsUnloaded ||
                workspaceCoordinator.MoveSessionToNewWindow(this, session) ||
                !sessions.Contains(session))
            {
                throw new InvalidOperationException(
                    "An unloaded drawing was allowed to enter a transfer.");
            }

            var wakeTask = editorSessions.WakeAsync(session);
            if (!session.IsRestoringFromHibernation ||
                !session.IsResuming ||
                workspaceCoordinator.MoveSessionToNewWindow(this, session) ||
                !sessions.Contains(session))
            {
                throw new InvalidOperationException(
                    "A recreating drawing was allowed to enter a transfer.");
            }
            await wakeTask;
            if (!await AsyncWait.UntilAsync(
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

            await RunUnavailableTabTearOutSmokeAsync(session);
            multiWindowInitializationRelease = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var retryTask = editorSessions.RetryAsync(session);
            if (!await AsyncWait.UntilAsync(
                    () => session.IsInitializing,
                    TimeSpan.FromSeconds(5)) ||
                workspaceCoordinator.MoveSessionToNewWindow(this, session) ||
                !sessions.Contains(session))
            {
                throw new InvalidOperationException(
                    "An initializing drawing was allowed to enter a transfer.");
            }
            await RunUnavailableTabTearOutSmokeAsync(session);
            await PauseTearOutInteractionForSmokeAsync(session);
            multiWindowInitializationRelease.TrySetResult();
            multiWindowInitializationRelease = null;
            await retryTask;
            if (!await AsyncWait.UntilAsync(
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
            documents.AttachDocumentToSession(
                sourceSession,
                await sourceSession.DocumentService.OpenPathAsync(sourcePath),
                select: true);
            documents.AttachDocumentToSession(
                session,
                await session.DocumentService.OpenPathAsync(movedPath),
                select: false);
            if (!await AsyncWait.UntilAsync(
                    () => sourceSession.PendingDocumentLoad is null &&
                        session.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The multi-window file fixtures did not finish loading.");
            }
            var sourceWatcher = session.ExternalFileWatcher;
            DocumentTabs.SelectedItem = session.TabItem;
            if (!await AsyncWait.UntilAsync(
                    () => ReferenceEquals(ActiveSession, session),
                    TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException(
                    "The transfer fixture could not activate its drawing.");
            }
            RequestAutomationEdit(session, "multi-window-moved-edited");
            if (!await AsyncWait.UntilAsync(
                    () => session.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The transferred drawing did not become dirty.");
            }
            DocumentTabs.SelectedItem = sourceSession.TabItem;
            if (!await AsyncWait.UntilAsync(
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
            if (!await AsyncWait.UntilAsync(
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
            if (!await AsyncWait.UntilAsync(
                    () => sourceSession.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The source-window drawing did not become dirty.");
            }
            RequestSessionSave(sourceSession);
            if (!await AsyncWait.UntilAsync(
                    () => !sourceSession.IsDirty &&
                        FileContains(sourcePath, "multi-window-source-edited"),
                    TimeSpan.FromSeconds(20)) ||
                !FileContains(movedPath, "multi-window-moved-edited"))
            {
                throw new InvalidOperationException(
                    "Saving in the source window changed the destination file.");
            }

            RequestAutomationEdit(session, "multi-window-save-as-edited");
            if (!await AsyncWait.UntilAsync(
                    () => session.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The cross-window Save As fixture did not become dirty.");
            }
            session.DocumentService.SaveFileOverrideForSmoke =
                await StorageFile.GetFileFromPathAsync(saveAsPath);
            RequestSessionSave(session, reason: "saveAs");
            if (!await AsyncWait.UntilAsync(
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
            await destination.documents.CheckExternalFileStateAsync(session, showPrompt: false);
            if (session.ExternalFileState != ExternalFileState.Modified ||
                sourceSession.ExternalFileState != ExternalFileState.None)
            {
                throw new InvalidOperationException(
                    "An external modification crossed window ownership boundaries.");
            }
            await destination.documents.ReloadExternalFileAsync(session);
            if (!await AsyncWait.UntilAsync(
                    () => session.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The destination window could not reload its modified file.");
            }

            File.Move(saveAsPath, renamedPath, overwrite: true);
            await destination.documents.CheckExternalFileStateAsync(session, showPrompt: false);
            if (session.ExternalFileState is not
                (ExternalFileState.Moved or ExternalFileState.Deleted))
            {
                throw new InvalidOperationException(
                    "The destination window did not detect an external move.");
            }
            File.Move(renamedPath, saveAsPath, overwrite: true);
            var restored = await session.DocumentService.OpenPathAsync(saveAsPath);
            await session.DocumentService.RestoreActiveFileAsync(
                saveAsPath, restored.Stamp, restored.ContentHash);
            destination.documents.WatchExternalFile(session, saveAsPath);
            session.ExternalFileState = ExternalFileState.None;

            var restoredContent = await File.ReadAllTextAsync(saveAsPath);
            File.Delete(saveAsPath);
            await destination.documents.CheckExternalFileStateAsync(session, showPrompt: false);
            if (session.ExternalFileState != ExternalFileState.Deleted)
            {
                throw new InvalidOperationException(
                    "The destination window did not detect external deletion.");
            }
            await File.WriteAllTextAsync(saveAsPath, restoredContent);
            restored = await session.DocumentService.OpenPathAsync(saveAsPath);
            await session.DocumentService.RestoreActiveFileAsync(
                saveAsPath, restored.Stamp, restored.ContentHash);
            destination.documents.WatchExternalFile(session, saveAsPath);
            session.ExternalFileState = ExternalFileState.None;

            RequestAutomationEdit(session, "multi-window-recovery-edited");
            if (!await AsyncWait.UntilAsync(
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
            if (!await AsyncWait.UntilAsync(
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

            await RunTearOutPresentationSmokeAsync(session);
            Title = "Excalidraw Desktop — Multi-window smoke passed";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            DiagnosticLogService.Error("smoke.multi_window_failed", exception);
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
        if (!smoke.RunTitleBarSmoke || titleBarSmokeStarted)
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
            await editorSessions.RetryAsync(first);
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
            documents.WatchExternalFile(second, cleanupProbePath);
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
        if (smoke.PerformanceTabCount <= 0 || performanceSmokeStarted)
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
            if (sessions.Count != smoke.PerformanceTabCount ||
                sessions.Select(session => session.TabOrigin.Host).Distinct().Count() !=
                    smoke.PerformanceTabCount)
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
            if ((smoke.PerformanceSuspendInactive || smoke.PerformanceUnloadInactive) &&
                sessions.Count > 1)
            {
                DocumentTabs.SelectedItem = sessions[^1].TabItem;
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                foreach (var session in sessions.Where(session =>
                    !ReferenceEquals(session, ActiveSession)).ToArray())
                {
                    if (smoke.PerformanceUnloadInactive)
                    {
                        if (await editorSessions.UnloadAsync(session, requireIdle: false))
                        {
                            unloadedTabCount++;
                        }
                    }
                    else if (await editorSessions.SuspendAsync(session, requireIdle: false))
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
                tabCount = smoke.PerformanceTabCount,
                startupMilliseconds,
                averageSwitchMilliseconds = switchDurations.Average(),
                maximumSwitchMilliseconds = switchDurations.Max(),
                workingSetBytes = process.WorkingSet64,
                privateMemoryBytes = process.PrivateMemorySize64,
                uniqueOrigins = sessions.Count,
                suspendInactive = smoke.PerformanceSuspendInactive,
                suspendedTabCount,
                unloadInactive = smoke.PerformanceUnloadInactive,
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
        if (!smoke.RunSuspensionSmoke || suspensionSmokeStarted)
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
            documents.AttachDocumentToSession(
                unloading,
                await unloading.DocumentService.OpenPathAsync(largeScenePath),
                select: false);
            if (!await AsyncWait.UntilAsync(
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
            if (await editorSessions.UnloadAsync(unloading, requireIdle: true) ||
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
            var largeScene = await unloading.DocumentService.OpenPathAsync(largeScenePath);
            await unloading.DocumentService.RestoreActiveFileAsync(
                largeScenePath, largeScene.Stamp, largeScene.ContentHash);
            unloading.ExternalFileState = ExternalFileState.None;
            unloading.LastLifecycleFailure = null;

            sleeping.InactiveSince = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(6);
            if (!await editorSessions.SuspendAsync(sleeping, requireIdle: true) ||
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
                if (!await AsyncWait.UntilAsync(
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
                    await editorSessions.SuspendAsync(unloading, requireIdle: false);
                var unloadedForCycle = suspensionReady &&
                    await editorSessions.UnloadAsync(unloading, requireIdle: true);
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
            await VerifyEditorRetrySmokeAsync(sessions[1]);
            await VerifyDocumentDirtySmokeAsync(sessions[1]);
            await VerifyCloseTimeoutSmokeAsync(sessions[1]);
            await VerifyRecoveryConflictSmokeAsync();
            await VerifySaveRecoveryExportSmokeAsync(sessions[1]);
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
        string? IndexedDbValue,
        string ViewBackgroundColor);
#endif

#if DEBUG
    private void TryRunRecoverySnapshotSmoke()
    {
        if (!smoke.RunRecoverySmoke || recoverySmokeStarted)
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
        session.TryPostEditorMessage(
            BridgeEventJson.Create(
                "app.automationEditRequested",
                new { elementId = $"recovery-{session.RecoveryId}" }));
        await Task.CompletedTask;
    }

    private void TryRunImageExportSmoke()
    {
        if (!smoke.RunImageExportSmoke || imageExportSmokeStarted)
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
        if (!smoke.RunDocumentSafetySmoke || documentSafetySmokeStarted)
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

            documents.AttachDocumentToSession(
                first,
                await first.DocumentService.OpenPathAsync(firstPath),
                select: true);
            documents.AttachDocumentToSession(
                second,
                await second.DocumentService.OpenPathAsync(secondPath),
                select: false);
            if (!await AsyncWait.UntilAsync(
                    () => first.PendingDocumentLoad is null &&
                        second.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The document-safety drawings did not finish loading.");
            }

            await LoadAutomationSceneAsync(first, firstEdited);
            RequestSessionSave(first);
            if (!await AsyncWait.UntilAsync(
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

            // A browser reload must preserve the native target and the loaded
            // scene; otherwise the next save could overwrite it with an empty
            // editor. Exercise the real WebView reload path after a saved load.
            var beforeReload = await ReadSmokeStateAsync(first);
            first.CoreWebView!.Reload();
            await Task.Delay(250);
            if (!await AsyncWait.UntilAsync(
                    () => first.IsReady && first.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The editor did not recover after a browser reload.");
            }
            var afterReload = await ReadSmokeStateAsync(first);
            if (!afterReload.ElementIds.SequenceEqual(beforeReload.ElementIds) ||
                !string.Equals(afterReload.ViewBackgroundColor, beforeReload.ViewBackgroundColor,
                    StringComparison.Ordinal) ||
                !DesktopDocumentPath.Equals(first.DocumentService.DocumentPath, firstPath))
            {
                throw new InvalidOperationException(
                    "Browser reload lost the loaded scene or native file target.");
            }

            await LoadAutomationSceneAsync(second, secondEdited);
            RequestSessionSave(second);
            if (!await AsyncWait.UntilAsync(
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
            await documents.CheckExternalFileStateAsync(first, showPrompt: true);
            if (!await AsyncWait.UntilAsync(
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
            if (!await AsyncWait.UntilAsync(
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
            await documents.CheckExternalFileStateAsync(first, showPrompt: true);
            if (!await AsyncWait.UntilAsync(
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
            if (!await AsyncWait.UntilAsync(
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
            await documents.CheckExternalFileStateAsync(first, showPrompt: true);
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
            documents.AttachDocumentToSession(first, refreshedFirst, select: true);
            if (!await AsyncWait.UntilAsync(
                    () => first.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The conflict-resolution drawing could not be refreshed.");
            }

            File.Move(saveAsPath, movedPath, overwrite: true);
            if (!await AsyncWait.UntilAsync(
                    () => first.ExternalFileState is
                        ExternalFileState.Moved or ExternalFileState.Deleted,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "An externally moved drawing was not detected.");
            }

            File.Delete(secondPath);
            if (!await AsyncWait.UntilAsync(
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
        // Wait for the editor's actual scene rather than relying on a fixed
        // delay, which races background WebView scheduling on inactive tabs.
        using var expected = JsonDocument.Parse(content);
        var ids = expected.RootElement.GetProperty("elements").EnumerateArray()
            .Select(element => element.GetProperty("id").GetString()!).ToArray();
        session.TryPostEditorMessage(
            BridgeEventJson.Create(
                "document.loadRequested",
                new
                {
                    loadId = session.DocumentService.PendingOpenId ?? Guid.NewGuid(),
                    fileName = session.DisplayName,
                    content,
                    isRecovery = false,
                }));
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        do
        {
            var state = await ReadSmokeStateAsync(session);
            if (state.ElementIds.SequenceEqual(ids) &&
                await session.CoreWebView!.ExecuteScriptAsync(
                    "window.__EXCALIDRAW_DESKTOP_SMOKE__.isSaving()") == "false")
                return;
            await Task.Delay(50);
        } while (DateTimeOffset.UtcNow < deadline);
        throw new InvalidOperationException("The automation scene did not finish applying.");
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

#if DEBUG
    private void OnImageExportCompletedForSmoke(string path)
    {
        if (smoke.RunImageExportSmoke && imageExportSmokeOutputPath is { } outputPath &&
            DesktopDocumentPath.Equals(path, outputPath))
        {
            Title = "Excalidraw Desktop — Image export smoke passed";
        }
    }

    private bool OnImageExportFailedForSmoke(string message)
    {
        if (!smoke.RunImageExportSmoke)
        {
            return false;
        }
        Title = $"Excalidraw Desktop — Image export smoke failed: {message}";
        return true;
    }
#endif

    private void RequestSessionSave(
        DocumentSession session,
        string reason = "save")
    {
        session.TryPostEditorMessage(
            BridgeEventJson.Create(
                "document.saveRequested",
                new { reason }));
    }

    private void TryRunCloseDecisionsSmoke()
    {
        if (!smoke.RunCloseDecisionsSmoke || closeDecisionsSmokeStarted ||
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
            documents.AttachDocumentToSession(
                saveSession,
                await saveSession.DocumentService.OpenPathAsync(savedPath),
                select: false);
            if (!await AsyncWait.UntilAsync(
                    () => saveSession.PendingDocumentLoad is null,
                    TimeSpan.FromSeconds(20)))
            {
                throw new InvalidOperationException(
                    "The close-save drawing did not finish loading.");
            }

            RequestAutomationEdit(cancelAndDiscardSession, "close-cancel-discard");
            if (!await AsyncWait.UntilAsync(
                    () => cancelAndDiscardSession.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The cancel/discard drawing did not become dirty.");
            }

            Title = "Excalidraw Desktop — Close decisions smoke: choose Cancel";
            if (await windowClose.RequestCloseSessionAsync(cancelAndDiscardSession) ||
                !sessions.Contains(cancelAndDiscardSession) ||
                !cancelAndDiscardSession.IsDirty)
            {
                throw new InvalidOperationException(
                    "Cancel did not preserve the dirty drawing.");
            }

            Title = "Excalidraw Desktop — Close decisions smoke: choose Discard";
            if (!await windowClose.RequestCloseSessionAsync(cancelAndDiscardSession) ||
                sessions.Contains(cancelAndDiscardSession))
            {
                throw new InvalidOperationException(
                    "Discard did not close the dirty drawing.");
            }

            RequestAutomationEdit(saveSession, "close-save-edited");
            if (!await AsyncWait.UntilAsync(
                    () => saveSession.IsDirty,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException(
                    "The close-save drawing did not become dirty.");
            }

            Title = "Excalidraw Desktop — Close decisions smoke: choose Save";
            if (!await windowClose.RequestCloseSessionAsync(saveSession) ||
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
                await editorSessions.InitializeAsync(session);
                await File.WriteAllTextAsync(
                    windowClosePaths[index],
                    CreateDocumentSafetyScene($"window-close-initial-{index}", index));
                documents.AttachDocumentToSession(
                    session,
                    await session.DocumentService.OpenPathAsync(windowClosePaths[index]),
                    select: false);
            }
            if (!await AsyncWait.UntilAsync(
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
                if (!await AsyncWait.UntilAsync(
                        () => session.IsDirty,
                        TimeSpan.FromSeconds(10)))
                {
                    throw new InvalidOperationException(
                        $"Window-close drawing {index + 1} did not become dirty.");
                }
            }

            Title = "Excalidraw Desktop — Window close smoke: choose Cancel";
            if (await windowClose.ResolveWindowCloseAsync() ||
                sessions.Any(session => !session.IsDirty))
            {
                throw new InvalidOperationException(
                    "Window-close Cancel did not preserve every dirty drawing.");
            }

            var firstDirtySession = sessions[0];
            Title = "Excalidraw Desktop — Window close smoke: choose Review Tabs";
            if (await windowClose.ResolveWindowCloseAsync() ||
                !ReferenceEquals(ActiveSession, firstDirtySession) ||
                sessions.Any(session => !session.IsDirty))
            {
                throw new InvalidOperationException(
                    "Review Tabs did not preserve and select the first dirty drawing.");
            }

            Title = "Excalidraw Desktop — Window close smoke: choose Save All";
            if (!await windowClose.ResolveWindowCloseAsync() ||
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
        session.TryPostEditorMessage(
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
            documents.AttachDocumentToSession(session, document, select: true);
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
            imageExports.StartImageExport(session, coreWebView, destination);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Title = $"Excalidraw Desktop — Image export smoke failed: {exception.Message}";
        }
    }
#endif

}
