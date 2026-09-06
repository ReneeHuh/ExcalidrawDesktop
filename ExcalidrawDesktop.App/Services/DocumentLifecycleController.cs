using System.Diagnostics;
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App.Services;

internal interface IDocumentLifecycleHost
{
    DocumentSession? ActiveSession { get; }
    DocumentSession CreateTab(bool select = true);
    void AddRecentFile(string path);
    void UpdateTabHeader(DocumentSession session);
    void UpdateWindowTitle();
    void QueuePersistWorkspace();
    void QueueJumpListUpdate();
    bool CommandsBlocked { get; }
    WindowModalCoordinator Modals { get; }
}

/// <summary>Opens drawings, restores recovery content, and resolves disk conflicts for one window.</summary>
internal sealed class DocumentLifecycleController
{
    private readonly IDocumentLifecycleHost host;
    private readonly IReadOnlyList<DocumentSession> sessions;
    private readonly TabView documentTabs;
    private readonly ApplicationWorkspaceCoordinator workspaceCoordinator;
    private readonly RecoverySnapshotStore recoverySnapshotStore;
    private List<string> recentFiles => workspaceCoordinator.RecentFiles;

    public DocumentLifecycleController(IDocumentLifecycleHost host,
        IReadOnlyList<DocumentSession> sessions, TabView documentTabs,
        ApplicationWorkspaceCoordinator workspaceCoordinator)
    {
        this.host = host;
        this.sessions = sessions;
        this.documentTabs = documentTabs;
        this.workspaceCoordinator = workspaceCoordinator;
        recoverySnapshotStore = workspaceCoordinator.RecoverySnapshotStore;
    }

    // PNG export shares the native picker gate with document opening.
    public bool IsPickerActive { get; set; }

    public event Action<DocumentSession>? DocumentLoadTimedOut;

    private sealed class ReservationTransfer(IDisposable reservation) : IDisposable
    {
        private IDisposable? held = reservation;
        public IDisposable Take()
        {
            var value = held ?? throw new InvalidOperationException("Reservation already transferred.");
            held = null;
            return value;
        }
        public void Dispose() => held?.Dispose();
    }

#if DEBUG
    public Func<DocumentSession, Task>? SnapshotSavedForSmoke { get; set; }
#endif

    public async Task RestoreRecoveryAsync(DocumentSession session,
        string? path, string displayName, string content)
    {
        var baseline = RecoveryFileBaseline.Read(content, path);
        await session.DocumentService.RestoreActiveFileAsync(
            path, baseline?.Stamp, baseline?.ContentHash);
        session.ExternalFileState = await session.DocumentService.CheckExternalFileStateAsync();
        if (session.DocumentService.DocumentPath is { } recoveredPath)
        {
            WatchExternalFile(session, recoveredPath);
        }
        AttachRecoveryToSession(session, displayName, content);
    }

    public Task OpenPathInTabAsync(string path) => OpenPathInTabCoreAsync(path, fromRecent: false);

    private DocumentSession GetOpenTarget() => host.ActiveSession is { IsDirty: false } active &&
        active.DocumentService.DocumentPath is null && active.PendingDocumentLoad is null &&
        !active.DocumentService.IsSaving ? active : host.CreateTab();

    private async Task OpenPathInTabCoreAsync(string path, bool fromRecent)
    {
        if (host.CommandsBlocked || (fromRecent && IsPickerActive)) return;
        if (fromRecent) IsPickerActive = true;
        var fallback = fromRecent
            ? DesktopResources.Get("RecentDrawingOpenFailed", "The recent drawing could not be opened.")
            : DesktopResources.Get("ActivatedDrawingOpenFailed", "The activated drawing could not be opened.");
        try
        {
            var canonicalPath = DesktopDocumentPath.Normalize(path);
            var existing = workspaceCoordinator.FindSessionByPath(canonicalPath);
            if (existing is not null)
            {
                workspaceCoordinator.ActivateSession(existing.Value.Window, existing.Value.Session);
                host.AddRecentFile(path);
                return;
            }
            if (!workspaceCoordinator.TryReservePath(path, new object(), out var reservation))
                throw new BridgeProtocolException("DocumentAlreadyOpen", "The drawing is already being opened or saved.");
            using var openReservation = new ReservationTransfer(reservation!);
            var source = host.ActiveSession ?? sessions[0];
            var document = await source.DocumentService.OpenPathAsync(path);
            if (host.CommandsBlocked) return;
            existing = workspaceCoordinator.FindSessionByPath(canonicalPath);
            if (existing is not null)
            {
                workspaceCoordinator.ActivateSession(existing.Value.Window, existing.Value.Session);
                return;
            }
            AttachDocumentToSession(GetOpenTarget(), document, select: true, openReservation.Take());
        }
        catch (BridgeProtocolException exception)
        {
            if (fromRecent && exception.Code == "DocumentNotFound")
            {
                recentFiles.RemoveAll(recent => DesktopDocumentPath.Equals(recent, path));
                host.QueuePersistWorkspace();
                host.QueueJumpListUpdate();
            }
            await ShowOpenErrorAsync(GetLocalizedDocumentError(exception, fallback));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await ShowOpenErrorAsync(fallback);
        }
        finally { if (fromRecent) IsPickerActive = false; }
    }

    public async Task RequestOpenDocumentAsync(DocumentSession source)
    {
        if (host.CommandsBlocked || IsPickerActive || !sessions.Contains(source))
        {
            return;
        }

        IsPickerActive = true;
        try
        {
            var document = await source.DocumentService.PickOpenDocumentAsync();
            if (document is null)
            {
                return;
            }
            if (host.CommandsBlocked || !sessions.Contains(source)) return;

            var existing = workspaceCoordinator.FindSessionByPath(
                document.CanonicalPath);
            if (existing is not null)
            {
                workspaceCoordinator.ActivateSession(
                    existing.Value.Window,
                    existing.Value.Session);
                return;
            }

            var target = GetOpenTarget();
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
            IsPickerActive = false;
        }
    }

    public void AttachDocumentToSession(
        DocumentSession session,
        PickedDocument document,
        bool select,
        IDisposable? reservation = null)
    {
        try
        {
        if (!sessions.Contains(session) || session.DocumentService.IsSaving ||
            session.PendingDocumentLoad is not null)
            throw new BridgeProtocolException("DocumentLoadInProgress", "Wait for the drawing operation to finish.");
        if (workspaceCoordinator.IsPathOwnedByAnotherSession(session, document.CanonicalPath) ||
            (reservation is null && !workspaceCoordinator.TryReservePath(document.CanonicalPath, session, out reservation)))
            throw new BridgeProtocolException("DocumentAlreadyOpen", "The drawing is already being opened or saved.");
        session.DocumentService.StageOpen(document);
        session.PendingDocumentLoad = new PendingEditorLoad(
            document.FileName,
            document.Content,
            IsRecovery: false) { LoadId = session.DocumentService.PendingOpenId!.Value };
        session.PendingPathReservation = reservation;
        reservation = null;
        session.DisplayName = document.FileName;
        host.UpdateTabHeader(session);
        if (select)
        {
            documentTabs.SelectedItem = session.TabItem;
        }

        if (session.IsReady)
        {
            SendPendingDocumentLoad(session);
        }
        }
        finally
        {
            reservation?.Dispose();
        }
    }

    public void WatchExternalFile(DocumentSession session, string path)
    {
        session.DetachExternalFileWatcher();
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            session.ExternalFileWatcher = null;
            return;
        }

        var uiDispatcherQueue = documentTabs.DispatcherQueue;
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

    public void SendPendingDocumentLoad(DocumentSession session)
    {
        if (!session.IsReady ||
            session.CoreWebView is null ||
            session.PendingDocumentLoad is not { } document)
        {
            return;
        }

        if (document.LoadId == Guid.Empty)
        {
            document = document with { LoadId = session.DocumentService.PendingOpenId ?? Guid.NewGuid() };
            session.PendingDocumentLoad = document;
        }

        session.TryPostEditorMessage(
            BridgeEventJson.Create(
                "document.loadRequested",
                new
                {
                    loadId = document.LoadId,
                    fileName = document.FileName,
                    content = document.Content,
                    isRecovery = document.IsRecovery,
                }));
        _ = WatchDocumentLoadAsync(session, document.LoadId, session.CoreWebView);
    }

    private async Task WatchDocumentLoadAsync(DocumentSession session, Guid loadId,
        Microsoft.Web.WebView2.Core.CoreWebView2? editor)
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        if (sessions.Contains(session) && ReferenceEquals(session.CoreWebView, editor) &&
            session.PendingDocumentLoad?.LoadId == loadId)
        {
            session.TryPostEditorMessage(BridgeEventJson.Create("document.loadCancelled", new { loadId }));
            DocumentLoadTimedOut?.Invoke(session);
        }
    }

    public Task ShowOpenErrorAsync(string message) => host.Modals.ShowMessageAsync(
        DesktopResources.Get("OpenDrawingErrorTitle", "Could not open drawing"), message);

    private static string FileUnavailableMessage() => DesktopResources.Get("FileUnavailableContent", "The file could not be read. It may be busy or access may be denied. Retry, save to a different file, or keep editing.");
    private static string FileBaselineUnknownMessage() => DesktopResources.Get("FileBaselineUnknownContent", "This recovered drawing has no saved file baseline. Save it to a different file to keep your changes, or reload the disk version to replace this tab. The app cannot verify whether the original file changed.");

    public static string GetLocalizedDocumentError(
        BridgeProtocolException exception,
        string fallback) => exception.Code switch
        {
            "DocumentUnavailable" => FileUnavailableMessage(),
            "DocumentBaselineUnknown" => FileBaselineUnknownMessage(),
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

    public void AttachRecoveryToSession(
        DocumentSession session,
        string displayName,
        string content)
    {
        session.PendingDocumentLoad = new PendingEditorLoad(
            displayName,
            content,
            IsRecovery: true) { LoadId = Guid.NewGuid() };
        session.DisplayName = displayName;
        session.IsDirty = true;
        host.UpdateTabHeader(session);
    }

    public Task OpenRecentFileAsync(string path) => OpenPathInTabCoreAsync(path, fromRecent: true);

    public async Task CheckExternalFileStateAsync(
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
                !ReferenceEquals(session, host.ActiveSession))
            {
                session.InactiveSince = DateTimeOffset.UtcNow;
            }
            session.ExternalFileState = state;
            host.UpdateTabHeader(session);
            if (ReferenceEquals(session, host.ActiveSession))
            {
                host.UpdateWindowTitle();
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

    public async Task ShowExternalFileConflictAsync(DocumentSession session)
    {
        if (host.CommandsBlocked || host.Modals.IsBusy || !sessions.Contains(session) ||
            session.ExternalFileState is ExternalFileState.None ||
            session.ExternalConflictPromptOpen)
        {
            return;
        }

        session.ExternalConflictPromptOpen = true;
        documentTabs.SelectedItem = session.TabItem;
        try
        {
            (string Title, string Content, string Button, Func<Task> Apply) prompt = session.ExternalFileState switch
            {
                ExternalFileState.Unavailable => (
                    DesktopResources.Get("FileVerificationTitle", "File version could not be verified"),
                    FileUnavailableMessage(),
                    DesktopResources.Get("SettingsPersistenceRetryButton.Content", "Retry"),
                    () => CheckExternalFileStateAsync(session, showPrompt: false)),
                ExternalFileState.UnknownBaseline => (
                    DesktopResources.Get("FileVerificationTitle", "File version could not be verified"),
                    FileBaselineUnknownMessage(),
                    DesktopResources.Get("ReloadButton", "Reload"),
                    () => ReloadExternalFileAsync(session)),
                ExternalFileState.Deleted or ExternalFileState.Moved => (
                    DesktopResources.Get("DrawingFileMissingTitle", "Drawing file is missing"),
                    DesktopResources.Get("DrawingFileMissingContent", "The backing file was moved or deleted. Locate it, save this tab to a new file, or keep editing without overwriting anything."),
                    DesktopResources.Get("LocateFileButton", "Locate file"),
                    () => LocateExternalFileAsync(session)),
                _ => (
                    DesktopResources.Get("DrawingChangedOutsideTitle", "Drawing changed outside the app"),
                    DesktopResources.Get("DrawingChangedOutsideContent", "Reload the disk version, save this tab to a different file, or keep editing. The existing file will not be overwritten automatically."),
                    DesktopResources.Get("ReloadButton", "Reload"),
                    () => ReloadExternalFileAsync(session)),
            };
            var dialog = new ContentDialog
            {
                XamlRoot = documentTabs.XamlRoot,
                Title = prompt.Title,
                Content = prompt.Content,
                PrimaryButtonText = prompt.Button,
                SecondaryButtonText = DesktopResources.Get("SaveAsButton", "Save As"),
                CloseButtonText = DesktopResources.Get("KeepEditingButton", "Keep editing"),
                DefaultButton = ContentDialogButton.Close,
            };
            var result = await host.Modals.RunAsync(async () => await dialog.ShowAsync());
            if (!sessions.Contains(session) || host.CommandsBlocked) return;
            if (result == ContentDialogResult.Primary)
            {
                await prompt.Apply();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                session.TryPostEditorMessage(
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
            host.UpdateWindowTitle();
        }
    }

    public async Task ReloadExternalFileAsync(DocumentSession session)
    {
        var document = await session.DocumentService.ReloadActiveAsync();
        session.ExternalFileState = ExternalFileState.None;
        AttachDocumentToSession(session, document, select: true);
    }

    public async Task LocateExternalFileAsync(DocumentSession session)
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

#if DEBUG
    public Func<DocumentSession, Task>? BeforeRecoveryWriteForSmoke { get; set; }
#endif

    public async Task<bool> OnRecoverySnapshotReceivedAsync(
        DocumentSession session,
        string content)
    {
        if (!sessions.Contains(session) || !session.IsDirty)
        {
            return false;
        }

        await session.RecoveryGate.WaitAsync();
        try
        {
            if (sessions.Contains(session) && session.IsDirty)
            {
#if DEBUG
                if (BeforeRecoveryWriteForSmoke is { } beforeWrite)
                {
                    await beforeWrite(session);
                }
#endif
                await recoverySnapshotStore.SaveAsync(
                    session.RecoveryId, content, session.DocumentService.RecoveryBaseline);
                session.RecoveryFailed = false;
                session.RecoveryUpdatedAt = DateTimeOffset.UtcNow;
                host.QueuePersistWorkspace();
                if (ReferenceEquals(session, host.ActiveSession))
                {
                    host.UpdateWindowTitle();
                }
#if DEBUG
                if (SnapshotSavedForSmoke is { } snapshotSaved)
                {
                    await snapshotSaved(session);
                }
#endif
                return true;
            }
            return false;
        }
        catch (Exception exception)
        {
            session.RecoveryFailed = true;
            host.UpdateTabHeader(session);
            host.UpdateWindowTitle();
            DiagnosticLogService.Error("recovery.write_failed", exception);
            throw;
        }
        finally
        {
            session.RecoveryGate.Release();
        }
    }

    public async Task DeleteRecoverySnapshotAsync(DocumentSession session, bool onlyIfClean = false)
    {
        var recoveryId = session.RecoveryId;
        var version = session.DocumentStateVersion;
        await session.RecoveryGate.WaitAsync();
        try
        {
            if (onlyIfClean && (session.IsDirty || session.PendingDocumentLoad?.IsRecovery == true ||
                session.DocumentStateVersion != version || session.RecoveryId != recoveryId)) return;
            await recoverySnapshotStore.DeleteAsync(recoveryId);
            workspaceCoordinator.ReleasePreservedRecovery(recoveryId);
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

    public async Task RefreshRecoveryBaselineAsync(DocumentSession session)
    {
        var recoveryId = session.RecoveryId;
        var baseline = session.DocumentService.RecoveryBaseline;
        await session.RecoveryGate.WaitAsync();
        try
        {
            if (!sessions.Contains(session) || session.RecoveryId != recoveryId) return;
            if (await recoverySnapshotStore.LoadAsync(recoveryId) is { } content)
                await recoverySnapshotStore.SaveAsync(recoveryId, content, baseline);
        }
        catch (Exception exception)
        {
            session.RecoveryFailed = true;
            DiagnosticLogService.Error("recovery.baseline_refresh_failed", exception);
        }
        finally
        {
            session.RecoveryGate.Release();
            if (sessions.Contains(session)) host.UpdateWindowTitle();
        }
    }
}
