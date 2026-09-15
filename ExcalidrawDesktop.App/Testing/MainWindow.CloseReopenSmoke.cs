using System.Text.Json;
using ExcalidrawDesktop.App.Services.Workspace;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
#if DEBUG
    private static void CheckCloseSmoke(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task ReadyCloseSmokeAsync(MainWindow window, DocumentSession session)
    {
        window.SelectSession(session);
        await window.editorSessions.InitializeAsync(session);
        if (session.IsReady) window.documents.SendPendingDocumentLoad(session);
        CheckCloseSmoke(await AsyncWait.UntilAsync(() => session.IsReady && session.PendingDocumentLoad is null,
            TimeSpan.FromSeconds(20)), $"Editor failed to become ready (Ready={session.IsReady}, Loading={session.PendingDocumentLoad?.FileName}, Failure={session.LastLifecycleFailure}, Initializing={session.IsInitializing}).");
    }

    private static async Task SendCloseSmokeKeyAsync(DocumentSession session, string key, bool shift = false)
    {
        await session.CoreWebView!.ExecuteScriptAsync("(() => { const node = document.getElementById('root'); " +
            "const ownerWindow = node.ownerDocument.defaultView; node.dispatchEvent(new ownerWindow.KeyboardEvent('keydown', " +
            JsonSerializer.Serialize(new { key, ctrlKey = true, shiftKey = shift, bubbles = true, cancelable = true }) + ")); })()");
    }

    private async Task RunCloseDecisionsSmokeAsync()
    {
        var resultPath = Path.Combine(AppContext.BaseDirectory, "close-decisions-smoke.result");
        var errors = new List<string>();
        WindowModalCoordinator.For(this).MessageForSmoke = (title, _) => errors.Add(title);
        try
        {
            var draft = sessions[0];
            await ReadyCloseSmokeAsync(this, draft);
            RequestAutomationEdit(draft, "close-checkpoint-latest");
            CheckCloseSmoke(await AsyncWait.UntilAsync(() => draft.IsDirty, TimeSpan.FromSeconds(10)), "Draft did not become dirty.");
            await VerifyLibraryCloseCancellationAsync(draft);
            var recoveryId = draft.RecoveryId;
            CheckCloseSmoke(await windowClose.RequestCloseSessionAsync(draft), "Dirty tab did not close.");
            CheckCloseSmoke(!sessions.Contains(draft) && workspaceCoordinator.ClosedItems.Count == 1, "Tab history was not recorded.");
            CheckCloseSmoke((await recoverySnapshotStore.LoadAsync(recoveryId))?.Contains("close-checkpoint-latest") == true,
                "The final drawing edit was not checkpointed.");

            await ReadyCloseSmokeAsync(this, sessions[0]);
            await SendCloseSmokeKeyAsync(sessions[0], "t", shift: true);
            CheckCloseSmoke(await AsyncWait.UntilAsync(() => !workspaceCoordinator.IsBusy &&
                workspaceCoordinator.ClosedItems.Count == 0 && sessions.Any(s => s.RecoveryId == recoveryId), TimeSpan.FromSeconds(30)),
                "Ctrl+Shift+T did not reopen exactly one tab.");
            draft = sessions.Single(s => s.RecoveryId == recoveryId);
            CheckCloseSmoke(draft.IsDirty && draft.DocumentService.DocumentPath is null, "Reopened draft lost its unsaved state.");

            // A failed snapshot and a failed metadata replacement both keep the
            // live draft and leave history unchanged.
            documents.BeforeRecoveryWriteForSmoke = session => session.CloseBarrierId is not null
                ? Task.FromException(new IOException("Injected draft write failure")) : Task.CompletedTask;
            CheckCloseSmoke(!await windowClose.RequestCloseSessionAsync(draft) && sessions.Contains(draft), "Draft write failure closed the drawing.");
            documents.BeforeRecoveryWriteForSmoke = null;
            await workspaceCoordinator.PersistWorkspaceAsync();
            using (var locked = new FileStream(workspaceCoordinator.WorkspaceStatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                CheckCloseSmoke(!await windowClose.RequestCloseSessionAsync(draft) && sessions.Contains(draft), "Workspace write failure closed the drawing.");
            CheckCloseSmoke(errors.Count >= 2 && workspaceCoordinator.ClosedItems.Count == 0, "Failure reporting/history rollback failed.");
            await VerifyClosedFileRecoverySmokeAsync();

            // Ctrl+T and Ctrl+N have fixed meanings even with reopen history.
            await ReadyCloseSmokeAsync(this, draft);
            await SendCloseSmokeKeyAsync(draft, "t");
            CheckCloseSmoke(await AsyncWait.UntilAsync(() => sessions.Count == 3, TimeSpan.FromSeconds(10)), "Ctrl+T did not create one tab.");
            await ReadyCloseSmokeAsync(this, draft);
            await SendCloseSmokeKeyAsync(draft, "n");
            CheckCloseSmoke(await AsyncWait.UntilAsync(() => workspaceCoordinator.Windows.Count == 2 &&
                workspaceCoordinator.Windows.All(w => w.IsReadyForActivation), TimeSpan.FromSeconds(10)), "Ctrl+N did not create one window.");
            var other = workspaceCoordinator.Windows.Single(w => !ReferenceEquals(w, this));
            WindowModalCoordinator.For(other).MessageForSmoke = (title, _) => errors.Add(title);
            var otherDraft = other.sessions[0];
            await ReadyCloseSmokeAsync(other, otherDraft);
            await other.documents.RestoreRecoveryAsync(otherDraft, null, "Window draft", CreateDocumentSafetyScene("window-draft-latest", 1));
            await ReadyCloseSmokeAsync(other, otherDraft);
            var otherId = otherDraft.RecoveryId;
            other.CreateTab(select: false);

            // Cancellation in window two must leave window one open and unlocked.
            var releaseModal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var modal = WindowModalCoordinator.For(other).RunAsync(() => releaseModal.Task);
            await workspaceCoordinator.RequestExitAsync();
            CheckCloseSmoke(workspaceCoordinator.Windows.Count == 2 && !IsClosed && !other.IsClosed &&
                sessions.All(s => s.CloseBarrierId is null), "Exit partially closed windows after a later preparation failed.");
            releaseModal.SetResult(); await modal;

            CheckCloseSmoke(await other.RequestCloseAsync(), "Window did not close.");
            CheckCloseSmoke(workspaceCoordinator.Windows.Count == 1 && workspaceCoordinator.ClosedItems.Count == 1 &&
                workspaceCoordinator.ClosedItems[0].IsWindow && workspaceCoordinator.ClosedItems[0].Window.Tabs.Count == 2,
                "Closed window did not produce one grouped history entry.");
            await workspaceCoordinator.PruneRecoverySnapshotsAsync();
            CheckCloseSmoke((await recoverySnapshotStore.LoadAsync(otherId))?.Contains("window-draft-latest") == true, "Pruning removed a closed draft.");
            CheckCloseSmoke(await workspaceCoordinator.ReopenLastClosedAsync(this), "Grouped window could not be restored.");
            other = workspaceCoordinator.Windows.Single(w => !ReferenceEquals(w, this));
            CheckCloseSmoke(other.sessions.Count == 2 && other.sessions[0].RecoveryId == otherId && other.sessions[0].IsDirty,
                "Grouped window restoration lost order or the dirty draft.");

            // Close B again, open a new C, and Exit A+C. B must stay in history
            // across process restart, even with saved-tab reopening disabled.
            CheckCloseSmoke(await other.RequestCloseAsync(), "Restored window could not be closed again.");
            var third = workspaceCoordinator.CreateWindow();
            CheckCloseSmoke(await AsyncWait.UntilAsync(() => third.IsReadyForActivation, TimeSpan.FromSeconds(10)), "Third window did not start.");
            await ReadyCloseSmokeAsync(third, third.sessions[0]);
            workspaceCoordinator.UpdatePreferences(workspaceCoordinator.Preferences with { ReopenSavedTabs = false });
            await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "close-reopen-expected.json"),
                JsonSerializer.Serialize(workspaceCoordinator.Windows.Select(w => w.CaptureWorkspaceState()).ToArray()));
            await File.WriteAllTextAsync(resultPath, "exit-pending");
            await workspaceCoordinator.RequestExitAsync();
            CheckCloseSmoke(workspaceCoordinator.Windows.Count == 0, "Exit did not close all windows.");
        }
        catch (Exception exception)
        {
            documents.BeforeRecoveryWriteForSmoke = null;
            await File.WriteAllTextAsync(resultPath, "failed: " + exception);
        }
    }

    private async Task RunCloseReopenRestoreSmokeAsync()
    {
        var resultPath = Path.Combine(AppContext.BaseDirectory, "close-reopen-restore.result");
        try
        {
            var expected = JsonSerializer.Deserialize<WorkspaceWindowState[]>(await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "close-reopen-expected.json")))!;
            CheckCloseSmoke(await AsyncWait.UntilAsync(() => workspaceCoordinator.Windows.Count == expected.Length &&
                workspaceCoordinator.Windows.All(w => w.IsReadyForActivation), TimeSpan.FromSeconds(30)), "Exit workspace windows were not restored.");
            foreach (var window in workspaceCoordinator.Windows.ToArray())
            {
                var actual = window.CaptureWorkspaceState();
                var saved = expected.Single(w => w.Id == actual.Id);
                CheckCloseSmoke(actual.Tabs.Select(t => t.RecoveryId).SequenceEqual(saved.Tabs.Select(t => t.RecoveryId)), "Startup lost tab order or blank tabs.");
                CheckCloseSmoke(actual.ActiveRecoveryId == saved.ActiveRecoveryId, "Startup lost the selected drawing.");
                foreach (var session in window.sessions.ToArray()) await ReadyCloseSmokeAsync(window, session);
                CheckCloseSmoke(window.sessions.Select(s => s.IsDirty).SequenceEqual(saved.Tabs.Select(t => t.WasDirty)), "Startup lost unsaved state.");
            }
            CheckCloseSmoke(workspaceCoordinator.ClosedItems.Count == 1 && workspaceCoordinator.ClosedItems[0].IsWindow,
                "Previously closed B was resurrected automatically or lost from history.");
            CheckCloseSmoke(await workspaceCoordinator.ReopenLastClosedAsync(this) && workspaceCoordinator.Windows.Count == 3 &&
                workspaceCoordinator.ClosedItems.Count == 0, "B did not reopen exactly once after restart.");
            await File.WriteAllTextAsync(resultPath, "passed");
        }
        catch (Exception exception) { await File.WriteAllTextAsync(resultPath, "failed: " + exception); }
    }

    private async Task VerifyClosedFileRecoverySmokeAsync()
    {
        var path = Path.Combine(ExcalidrawDesktop.App.Services.Platform.DesktopPaths.DataRoot, "closed-file.excalidraw");
        var disk = CreateDocumentSafetyScene("original-on-disk", 0);
        await File.WriteAllTextAsync(path, disk);
        var fileTab = CreateTab();
        await ReadyCloseSmokeAsync(this, fileTab);
        documents.AttachDocumentToSession(fileTab, await fileTab.DocumentService.OpenPathAsync(path), select: true);
        await ReadyCloseSmokeAsync(this, fileTab);
        RequestAutomationEdit(fileTab, "unsaved-file-edit");
        CheckCloseSmoke(await AsyncWait.UntilAsync(() => fileTab.IsDirty, TimeSpan.FromSeconds(10)), "Saved file did not become dirty.");
        CheckCloseSmoke(await windowClose.RequestCloseSessionAsync(fileTab), "Dirty file tab did not close.");
        CheckCloseSmoke(await File.ReadAllTextAsync(path) == disk, "Closing overwrote the original file.");
        var historyId = workspaceCoordinator.ClosedItems[0].Id;
        var draftId = fileTab.RecoveryId;

        var existing = CreateTab();
        await ReadyCloseSmokeAsync(this, existing);
        documents.AttachDocumentToSession(existing, await existing.DocumentService.OpenPathAsync(path), select: true);
        await ReadyCloseSmokeAsync(this, existing);
        var count = sessions.Count;
        CheckCloseSmoke(await workspaceCoordinator.ReopenLastClosedAsync(this) && sessions.Count == count &&
            ReferenceEquals(ActiveSession, existing) && workspaceCoordinator.ClosedItems[0].Id == historyId,
            "Reopening a duplicate created another file owner or discarded its separate draft.");
        // Close the duplicate, then choose the older dirty draft from Recently closed.
        CheckCloseSmoke(await windowClose.RequestCloseSessionAsync(existing), "Existing file did not close.");
        CheckCloseSmoke(await workspaceCoordinator.ReopenLastClosedAsync(this, historyId), "Selecting an older draft failed.");
        var restored = sessions.Single(s => s.RecoveryId == draftId);
        CheckCloseSmoke(restored.IsDirty && await File.ReadAllTextAsync(path) == disk, "Reopening dirty file changed the original.");
        await SendCloseSmokeKeyAsync(restored, "s");
        CheckCloseSmoke(await AsyncWait.UntilAsync(() => !restored.IsDirty && FileContains(path, "unsaved-file-edit"),
            TimeSpan.FromSeconds(10)), "Explicit Save did not write the recovered edits to their file.");
        // Remove the live test tab without creating a user close entry.
        CloseSession(restored, discardRecovery: false);

        // The clean entry remains. A missing file must not consume it; when the
        // file returns, reopening must load its latest disk version.
        var cleanId = workspaceCoordinator.ClosedItems[0].Id;
        File.Delete(path);
        CheckCloseSmoke(!await workspaceCoordinator.ReopenLastClosedAsync(this) &&
            workspaceCoordinator.ClosedItems[0].Id == cleanId, "Missing file lost its history entry.");
        await File.WriteAllTextAsync(path, CreateDocumentSafetyScene("latest-disk-version", 10));
        CheckCloseSmoke(await workspaceCoordinator.ReopenLastClosedAsync(this), "Returning file could not reopen.");
        var clean = sessions.Single(s => DesktopDocumentPath.Equals(s.DocumentService.DocumentPath, path));
        CheckCloseSmoke(!clean.IsDirty, "Latest disk version was marked as an unsaved draft.");
        CloseSession(clean);
        CheckCloseSmoke(workspaceCoordinator.ClosedItems.Count == 0, "File recovery left duplicate history entries.");
        File.Delete(path);
    }
#endif
}
