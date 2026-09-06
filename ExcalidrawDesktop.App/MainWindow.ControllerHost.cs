using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;

namespace ExcalidrawDesktop.App;

// Window and tab UI operations used by the lifecycle controllers. The
// controllers own their workflows and never depend on the MainWindow type.
public sealed partial class MainWindow : IDocumentLifecycleHost, IWindowCloseHost, IImageExportHost
{
    DocumentSession? IDocumentLifecycleHost.ActiveSession => ActiveSession;
    DocumentSession IDocumentLifecycleHost.CreateTab(bool select) => CreateTab(select);
    void IDocumentLifecycleHost.AddRecentFile(string path) => AddRecentFile(path);
    void IDocumentLifecycleHost.UpdateTabHeader(DocumentSession session) => UpdateTabHeader(session);
    void IDocumentLifecycleHost.UpdateWindowTitle() => UpdateWindowTitle();
    void IDocumentLifecycleHost.QueuePersistWorkspace() => QueuePersistWorkspace();
    void IDocumentLifecycleHost.QueueJumpListUpdate() => QueueJumpListUpdate();
    bool IDocumentLifecycleHost.CommandsBlocked => CommandsBlocked;
    WindowModalCoordinator IDocumentLifecycleHost.Modals => WindowModalCoordinator.For(this);

    bool IWindowCloseHost.SaveDirtyDrawingsOnClose => desktopPreferences.SaveDirtyDrawingsOnClose;
    Task<bool> IWindowCloseHost.YieldToDispatcherAsync() => YieldToDispatcherAsync();
    void IWindowCloseHost.CloseSession(DocumentSession session) => CloseSession(session);
    void IWindowCloseHost.UpdateTabHeader(DocumentSession session) => UpdateTabHeader(session);
    Task IWindowCloseHost.ShowImageExportErrorAsync(string message) => imageExports.ShowImageExportErrorAsync(message);
    Task IWindowCloseHost.PersistWorkspaceAsync() => workspaceCoordinator.PersistWorkspaceAsync();
    Task IWindowCloseHost.PruneRecoverySnapshotsAsync() => workspaceCoordinator.PruneRecoverySnapshotsAsync();
    void IWindowCloseHost.RecordWindowDiscarded() => workspaceCoordinator.RecordWindowDiscarded(this);
    void IWindowCloseHost.ReleasePreservedRecovery(string recoveryId) => workspaceCoordinator.ReleasePreservedRecovery(recoveryId);
    WindowModalCoordinator IWindowCloseHost.Modals => WindowModalCoordinator.For(this);
    Task<bool> IWindowCloseHost.EnterCloseBarrierAsync(IReadOnlyList<DocumentSession> targets, bool closingWindow) => EnterCloseBarrierAsync(targets, closingWindow);
    void IWindowCloseHost.ExitCloseBarrier(IEnumerable<DocumentSession> targets) => ExitCloseBarrier(targets);

    Microsoft.UI.Xaml.Window IImageExportHost.OwnerWindow => this;
    DocumentSession? IImageExportHost.ActiveSession => ActiveSession;
    bool IImageExportHost.IsDisposed => resourcesDisposed;
    void IImageExportHost.UpdateFileMenuState(DocumentSession? session) => UpdateFileMenuState(session);
    void IImageExportHost.UpdateStatusBar(DocumentSession session) => UpdateStatusBar(session);
}
