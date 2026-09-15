using ExcalidrawDesktop.App.Services.Logging;
using ExcalidrawDesktop.App.Services.Editor;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App.Services.Workspace;

internal interface IWindowCloseHost
{
    Task<bool> YieldToDispatcherAsync();
    void CloseSession(DocumentSession session);
    void UpdateTabHeader(DocumentSession session);
    Task ShowImageExportErrorAsync(string message);
    Task<bool> CommitTabCloseAsync(DocumentSession session);
    WindowModalCoordinator Modals { get; }
    Task<bool> EnterCloseBarrierAsync(IReadOnlyList<DocumentSession> targets, bool closingWindow);
    void ExitCloseBarrier(IEnumerable<DocumentSession> targets);
}

/// <summary>Coordinates frozen draft checkpoints for closing and explicit Save All.</summary>
internal sealed class WindowCloseController
{
    private readonly IWindowCloseHost host;
    private readonly IReadOnlyList<DocumentSession> sessions;
    private readonly TabView documentTabs;

    public WindowCloseController(IWindowCloseHost host,
        IReadOnlyList<DocumentSession> sessions, TabView documentTabs)
    {
        this.host = host;
        this.sessions = sessions;
        this.documentTabs = documentTabs;
    }

    private TimeSpan SaveResponseTimeout { get; set; } = TimeSpan.FromSeconds(30);
#if DEBUG
    public TimeSpan SaveResponseTimeoutForSmoke
    {
        get => SaveResponseTimeout;
        set => SaveResponseTimeout = value;
    }
    public bool SuppressSaveRequestForSmoke { get; set; }
#endif

    public void OnCloseReady(DocumentSession session, Guid requestId)
    {
        if (!sessions.Contains(session) || session.CloseRequestId != requestId)
        {
            return;
        }
        (session.WindowCloseSaveCompletion ?? session.CloseCompletion)?.TrySetResult(!session.IsDirty);
    }

    public void OnCloseCancelled(DocumentSession session, Guid requestId)
    {
        if (session.CloseRequestId == requestId)
        {
            OnCloseCancelled(session);
        }
    }

    public void OnCloseCancelled(DocumentSession session)
    {
        session.CloseRequestId = null;
        var saveCompletion = session.WindowCloseSaveCompletion;
        var completion = session.CloseCompletion;
        session.WindowCloseSaveCompletion = null;
        session.CloseCompletion = null;
        saveCompletion?.TrySetResult(false);
        completion?.TrySetResult(false);
    }

    private async Task<bool> RequestCloseSaveAsync(DocumentSession session, bool closeTab)
    {
        if (session.CloseCompletion is not null || session.WindowCloseSaveCompletion is not null)
        {
            return false;
        }
        var requestId = Guid.NewGuid();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.CloseRequestId = requestId;
        if (closeTab)
        {
            session.CloseCompletion = completion;
        }
        else
        {
            session.WindowCloseSaveCompletion = completion;
        }
        try
        {
            var shouldPost = true;
#if DEBUG
            shouldPost = !SuppressSaveRequestForSmoke;
#endif
            if (shouldPost && !session.TryPostEditorMessage(BridgeEventJson.Create(
                    "document.saveRequested", new { reason = "close", closeRequestId = requestId })))
            {
                return false;
            }
            var saved = await CloseSaveWait.UntilAsync(completion.Task, SaveResponseTimeout,
                () => session.DocumentService.IsSavePickerOpen);
            if (!saved && !completion.Task.IsCompleted)
            {
                AppLogger.Warning($"[WindowCloseController] Window close save timeout (SessionId={session.RecoveryId})");
                session.Dispatcher.CancelCloseSave(requestId);
                session.TryPostEditorMessage(BridgeEventJson.Create(
                    "document.saveCancelled", new { closeRequestId = requestId }));
            }
            if (!saved || !sessions.Contains(session) || session.IsDirty)
            {
                return false;
            }
            if (closeTab)
            {
                host.CloseSession(session);
            }
            return true;
        }
        finally
        {
            // A cancelled or timed-out request must not consume a later acknowledgement.
            if (session.CloseRequestId == requestId)
            {
                OnCloseCancelled(session);
            }
        }
    }

    public async Task<bool> RequestCloseSessionAsync(DocumentSession session)
    {
        if (session.CloseBarrierId is not null) return false;
        try
        {
            return await host.EnterCloseBarrierAsync([session], closingWindow: false) &&
                await RequestCloseSessionCoreAsync(session);
        }
        finally
        {
            host.ExitCloseBarrier([session]);
        }
    }

    private async Task<bool> RequestCloseSessionCoreAsync(DocumentSession session)
    {
        // Removing and disposing a TabViewItem while WinUI is routing the
        // pointer/click event that targeted it can invalidate the input tree.
        // Keep this deferral inside the shared close path so bulk and future
        // callers receive the same protection.
        if (!await host.YieldToDispatcherAsync())
        {
            return false;
        }

        if (!sessions.Contains(session) || session.ClosePromptOpen)
        {
            return false;
        }

        if (session.IsExporting)
        {
            await host.ShowImageExportErrorAsync(
                DesktopResources.Get(
                    "WaitForDrawingExport",
                    "Wait for the current PNG export to finish before closing this drawing."));
            return false;
        }

        return await host.CommitTabCloseAsync(session);
    }

    public async Task<bool> ResolveWindowCloseAsync()
    {
        if (sessions.Any(session => session.CloseBarrierId is not null)) return false;
        var targets = sessions.ToArray();
        var approved = false;
        try
        {
            approved = await host.EnterCloseBarrierAsync(targets, closingWindow: true) &&
                CanCommitClose(targets);
            return approved;
        }
        finally
        {
            if (!approved)
            {
                host.ExitCloseBarrier(targets);
            }
        }
    }

    public static bool CanCommitClose(IEnumerable<DocumentSession> targets) =>
        targets.All(session => session.CloseBarrierId is not null &&
            session.CloseCheckpointVersion == session.DocumentStateVersion &&
            session.PendingDocumentLoad is null && session.LastLifecycleFailure is null &&
            (!session.HasUnsavedLibrary || session.DiscardLibraryOnClose) &&
            !session.DocumentService.IsSaving && !session.IsExporting &&
            (!session.IsDirty || (!session.RecoveryFailed && session.RecoveryUpdatedAt is not null)));

    public async Task<bool> SaveAllForWindowCloseAsync(
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

                documentTabs.SelectedItem = session.TabItem;
                return await RequestSaveForWindowCloseAsync(session);
            });

        return completed && sessions.All(session => !session.IsDirty);
    }

    public async Task<bool> RequestSaveForWindowCloseAsync(
        DocumentSession session)
    {
        if (!sessions.Contains(session) || !session.IsDirty)
        {
            return true;
        }

        if (session.WindowCloseSaveCompletion is not null ||
            session.CloseCompletion is not null)
        {
            return false;
        }

        // The caller selected the tab, which starts a lazy editor
        // initialization for tabs restored from recovery; give it a bounded
        // chance to become ready before deciding the save cannot run.
        if (!EditorSessionController.CanRequestSave(session) &&
            !await AsyncWait.UntilAsync(
                () => !sessions.Contains(session) || EditorSessionController.CanRequestSave(session),
                TimeSpan.FromSeconds(10)))
        {
            return false;
        }

        if (!sessions.Contains(session) || !EditorSessionController.CanRequestSave(session))
        {
            return !session.IsDirty;
        }

        try
        {
            return await RequestCloseSaveAsync(session, closeTab: false);
        }
        catch (Exception exception)
        {
            AppLogger.Error($"[WindowCloseController] RequestSaveForWindowCloseAsync failed (SessionId={session.RecoveryId})", exception);
            return false;
        }
    }
}
