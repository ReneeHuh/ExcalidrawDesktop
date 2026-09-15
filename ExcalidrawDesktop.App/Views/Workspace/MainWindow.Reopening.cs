using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Services.Workspace;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    internal bool IsCloseOrModalPending => windowClosePromptOpen || restoringWorkspace ||
        sessions.Any(session => session.CloseBarrierId is not null) || WindowModalCoordinator.For(this).IsBusy;
    internal DocumentSession? SelectedDrawing => ActiveSession;

    internal async Task RestoreClosedTabsAsync(
        IReadOnlyList<(WorkspaceTabState Tab, string? Content)> tabs,
        int insertionIndex, string? activeRecoveryId, List<DocumentSession> created)
    {
        foreach (var (tab, content) in tabs)
        {
            var session = CreateTab(select: false);
            created.Add(session);
            // Provisional tabs are not editable until history consumption and
            // the new active workspace have been committed together.
            session.Content.IsEnabled = false;
            session.RecoveryId = tab.RecoveryId ?? session.RecoveryId;
            session.RecoveryUpdatedAt = tab.RecoveryUpdatedAt;
            if (tab.WasDirty)
                await documents.RestoreRecoveryAsync(session, tab.Path,
                    tab.DisplayName ?? NextUntitledName(), content!);
            else if (tab.Path is { } path)
                documents.AttachDocumentToSession(session, await session.DocumentService.OpenPathAsync(path), select: false);
            else if (tab.DisplayName is { } name) session.DisplayName = name;

            DocumentTabs.TabItems.Remove(session.TabItem);
            DocumentTabs.TabItems.Insert(Math.Clamp(insertionIndex++, 0, DocumentTabs.TabItems.Count), session.TabItem);
            SelectSession(session);
            await editorSessions.InitializeAsync(session);
            // An already-ready editor needs the staged recovery explicitly sent.
            if (session.IsReady) documents.SendPendingDocumentLoad(session);
            if (!await AsyncWait.UntilAsync(() => session.LastLifecycleFailure is not null ||
                (session.IsReady && session.PendingDocumentLoad is null), TimeSpan.FromSeconds(30)) ||
                session.LastLifecycleFailure is not null)
                throw new IOException("The editor could not restore the drawing.");
            UpdateTabHeader(session);
        }
        SelectSession(created.FirstOrDefault(session => session.RecoveryId == activeRecoveryId) ?? created[0]);
    }
}
