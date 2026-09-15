using ExcalidrawDesktop.App.Services.Logging;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App.Services.Workspace;

internal sealed partial class ApplicationWorkspaceCoordinator
{
    public async Task<bool> CommitTabCloseAsync(MainWindow window, DocumentSession session)
    {
        if (IsBusy) return false;
        transitionInProgress = true;
        await persistenceGate.WaitAsync();
        try
        {
            if (!WindowCloseController.CanCommitClose([session])) return false;
            var state = WorkspaceCloseHistory.CloseTab(CaptureWorkspace(), GetLogicalWindowId(window), session.RecoveryId);
            await workspaceStateStore.SaveAsync(state);
            if (!WindowCloseController.CanCommitClose([session]))
            {
                await workspaceStateStore.SaveAsync(CaptureWorkspace());
                return false;
            }
            ClosedItems = state.ClosedItems;
            window.CommitPreparedTabClose(session);
            AppLogger.Info("[ApplicationWorkspaceCoordinator] Drawing closed with recovery history");
            return true;
        }
        catch (Exception exception)
        {
            await ReportCloseFailureAsync(window, exception);
            return false;
        }
        finally { transitionInProgress = false; persistenceGate.Release(); }
    }

    public Task<bool> RequestWindowCloseAsync(MainWindow window) => CloseWindowsAsync([window], exiting: false);

    public async Task RequestExitAsync() => await CloseWindowsAsync(windows.ToArray(), exiting: true);

    private async Task<bool> CloseWindowsAsync(MainWindow[] targets, bool exiting)
    {
        if (IsBusy || targets.Length == 0) return false;
        transitionInProgress = true;
        isExiting = exiting;
        queuedPersistence?.Cancel();
        var prepared = new List<MainWindow>();
        await persistenceGate.WaitAsync();
        try
        {
            // Prepare every window before committing or disposing any of them.
            foreach (var window in targets)
            {
                if (!windows.Contains(window) || !await window.PrepareCloseAsync()) return false;
                prepared.Add(window);
            }
            if (targets.Any(window => !window.CanCommitPreparedClose)) return false;
            var before = CaptureWorkspace();
            var state = exiting ? WorkspaceCloseHistory.Exit(before)
                : WorkspaceCloseHistory.CloseWindow(before, GetLogicalWindowId(targets[0]));
            await workspaceStateStore.SaveAsync(state);
            if (targets.Any(window => !window.CanCommitPreparedClose))
            {
                await workspaceStateStore.SaveAsync(CaptureWorkspace());
                return false;
            }
            ClosedItems = state.ClosedItems;
            if (exiting || windows.Count == 1) terminalWorkspace = state;
            foreach (var window in targets) window.CommitPreparedClose();
            AppLogger.Info($"[ApplicationWorkspaceCoordinator] Close committed (WindowCount={targets.Length}, Exit={exiting})");
            if (windows.Count == 0)
            {
                DesktopLogging.Flush();
                Microsoft.UI.Xaml.Application.Current.Exit();
            }
            return true;
        }
        catch (Exception exception)
        {
            await ReportCloseFailureAsync(targets.FirstOrDefault(window => !window.IsClosed), exception);
            return false;
        }
        finally
        {
            foreach (var window in prepared.Where(window => !window.IsClosed)) window.CancelPreparedClose();
            isExiting = false;
            transitionInProgress = false;
            persistenceGate.Release();
            if (windows.Count > 0) QueuePersistWorkspace();
        }
    }

    private static Task ReportCloseFailureAsync(MainWindow? window, Exception exception)
    {
        AppLogger.Error("[ApplicationWorkspaceCoordinator] Could not commit close; drawings remain open", exception);
        return window is null ? Task.CompletedTask : WindowModalCoordinator.For(window).ShowMessageAsync(
            DesktopResources.Get("CloseNotReadyTitle", "Could not close drawing"),
            DesktopResources.Get("DraftCheckpointFailed", "The recovery draft or workspace could not be saved. Your drawings are still open. Free disk space or check access to the app's data folder, then try again."));
    }
}
