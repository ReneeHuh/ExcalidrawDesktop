using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private bool CommandsBlocked => resourcesDisposed || windowClosePromptOpen ||
        workspaceCoordinator.IsExiting || sessions.Any(session => session.CloseBarrierId is not null);

    private async Task<bool> EnterCloseBarrierAsync(IReadOnlyList<DocumentSession> targets)
    {
        if (restoringWorkspace || WindowModalCoordinator.For(this).IsBusy ||
            targets.Any(session => session.CloseBarrierId is not null || session.IsExporting ||
                session.IsMoving || session.IsUnloading))
        {
            return false;
        }

        foreach (var session in targets)
        {
            session.CloseBarrierId = Guid.NewGuid();
            session.CloseBarrierCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (session.Content.HasEditor)
            {
                session.Content.IsEnabled = false;
            }
        }

        try
        {
            foreach (var session in targets)
            {
                // A never-mounted clean tab has no editable scene to freeze. Dirty
                // recovery tabs are initialized so close-save can capture their scene.
                if (!session.IsReady && !session.IsInitializing && !session.IsDirty)
                {
                    continue;
                }
                if (session.IsSuspended)
                {
                    editorSessions.Resume(session);
                }
                if (session.IsUnloaded)
                {
                    await editorSessions.WakeAsync(session);
                }
                else if (!session.IsReady)
                {
                    await editorSessions.InitializeAsync(session);
                }

                if (!await AsyncWait.UntilAsync(
                    () => !sessions.Contains(session) || session.LastLifecycleFailure is not null ||
                        (session.IsReady && session.PendingDocumentLoad is null && !session.IsResuming),
                    TimeSpan.FromSeconds(30)) || !sessions.Contains(session))
                {
                    return false;
                }
                if (session.LastLifecycleFailure is not null)
                {
                    // A failed page cannot acknowledge. Stop its callbacks before
                    // presenting the existing save/discard/cancel recovery decision.
                    EditorSessionController.DetachWebView(session);
                    continue;
                }

                session.Content.IsEnabled = false;
                if (!session.TryPostEditorMessage(BridgeEventJson.Create(
                    "document.closeBarrierRequested",
                    new { barrierId = session.CloseBarrierId, locked = true })) ||
                    !await CloseSaveWait.UntilAsync(session.CloseBarrierCompletion!.Task,
                        TimeSpan.FromSeconds(30), () => session.DocumentService.IsSavePickerOpen))
                {
                    return false;
                }
            }
            return true;
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("window.close_barrier_failed", exception);
            return false;
        }
    }

    private void ExitCloseBarrier(IEnumerable<DocumentSession> targets)
    {
        foreach (var session in targets)
        {
            if (session.CloseBarrierId is not { } barrierId)
            {
                continue;
            }
            session.TryPostEditorMessage(BridgeEventJson.Create(
                "document.closeBarrierRequested", new { barrierId, locked = false }));
            session.CloseBarrierId = null;
            session.CloseBarrierCompletion?.TrySetResult(false);
            session.CloseBarrierCompletion = null;
            if (sessions.Contains(session) && session.Content.HasEditor)
            {
                session.Content.IsEnabled = true;
            }
        }
    }
}
