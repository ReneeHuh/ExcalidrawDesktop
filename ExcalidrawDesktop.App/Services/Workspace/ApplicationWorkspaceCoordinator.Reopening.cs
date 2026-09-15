using ExcalidrawDesktop.App.Services.Logging;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App.Services.Workspace;

internal sealed partial class ApplicationWorkspaceCoordinator
{
    public async Task<bool> ReopenLastClosedAsync(MainWindow requester, string? itemId = null)
    {
        var item = itemId is null ? ClosedItems.FirstOrDefault() : ClosedItems.FirstOrDefault(entry => entry.Id == itemId);
        if (IsBusy || item is null ||
            windows.Any(window => window.IsCloseOrModalPending)) return false;
        transitionInProgress = true;
        await persistenceGate.WaitAsync();
        MainWindow? target = null;
        var created = new List<DocumentSession>();
        var previousSelection = requester.SelectedDrawing;
        var duplicateDrafts = new List<WorkspaceTabState>();
        var duplicates = 0;
        var committed = false;
        try
        {
            var tabs = new List<(WorkspaceTabState Tab, string? Content)>();
            // Read and validate every required source before creating any tabs.
            // A missing file leaves the grouped history entry intact.
            foreach (var tab in item.Window.Tabs)
            {
                if (FindSessionByPath(tab.Path) is { } existing)
                {
                    existing.Window.SelectSession(existing.Session);
                    existing.Window.Activate();
                    duplicates++;
                    if (tab.WasDirty) duplicateDrafts.Add(tab);
                    continue;
                }
                string? content = null;
                if (tab.WasDirty)
                {
                    content = tab.RecoveryId is { } id ? await RecoverySnapshotStore.LoadAsync(id) : null;
                    if (content is null) throw new IOException("The recovery draft is unavailable.");
                    ExcalidrawDocumentValidator.Validate(content);
                }
                else if (tab.Path is { } path)
                {
                    var info = new FileInfo(path);
                    if (!info.Exists || info.Length > 50 * 1024 * 1024)
                        throw new IOException("The drawing is missing or exceeds the document size limit.");
                    ExcalidrawDocumentValidator.Validate(await File.ReadAllTextAsync(path));
                }
                tabs.Add((tab, content));
            }
            if (tabs.Count > 0)
            {
                target = item.IsWindow ? CreateWindow(createInitialTab: false) : requester;
                if (!await AsyncWait.UntilAsync(() => target.IsReadyForActivation, TimeSpan.FromSeconds(30)))
                    throw new IOException("The window did not finish opening.");
                if (item.IsWindow) target.RestoreWindowPlacement(item.Window);
                await target.RestoreClosedTabsAsync(tabs, item.TabIndex, item.Window.ActiveRecoveryId, created);
            }
            var remaining = ClosedItems.Where(entry => entry.Id != item.Id).ToList();
            // Never throw away a retained dirty draft just because its file is
            // already open. It may contain different edits from the live tab.
            if (duplicateDrafts.Count > 0)
                remaining.Insert(0, item with { Window = item.Window with { Tabs = duplicateDrafts.ToArray() } });
            var state = CaptureWorkspace() with { ClosedItems = remaining };
            await workspaceStateStore.SaveAsync(state);
            ClosedItems = remaining;
            committed = true;
            foreach (var session in created) session.Content.IsEnabled = true;
            target?.Activate();
            AppLogger.Info($"[ApplicationWorkspaceCoordinator] Closed item restored (Tabs={created.Count}, Duplicates={duplicates})");
        }
        catch (Exception exception)
        {
            if (committed)
            {
                // Activation is presentation work after the durable restore.
                // Never roll back live tabs once their history was consumed.
                AppLogger.Error("[ApplicationWorkspaceCoordinator] Restored workspace could not be activated", exception);
                return true;
            }
            if (target is not null)
            {
                if (item.IsWindow) target.CommitPreparedClose();
                else
                {
                    foreach (var session in created) target.CommitPreparedTabClose(session);
                    if (previousSelection is not null) requester.SelectSession(previousSelection);
                }
            }
            AppLogger.Error("[ApplicationWorkspaceCoordinator] Reopen failed; history retained", exception);
            await WindowModalCoordinator.For(requester).ShowMessageAsync(
                DesktopResources.Get("ReopenFailedTitle", "Could not reopen drawing"),
                DesktopResources.Get("ReopenFailedContent", "A drawing or recovery draft could not be opened. The closed item is still in history. Check that its file is available, then try again."));
            return false;
        }
        finally
        {
            transitionInProgress = false;
            persistenceGate.Release();
            QueuePersistWorkspace();
        }
        if (duplicates > 0)
            await WindowModalCoordinator.For(target ?? requester).ShowMessageAsync(
                DesktopResources.Get("ReopenAlreadyOpenTitle", "Drawing already open"),
                DesktopResources.Get("ReopenAlreadyOpenContent", "Drawings that are already open were selected in their existing windows. Any separate unsaved drafts remain in history; close the existing drawing before reopening its draft."));
        return true;
    }
}
