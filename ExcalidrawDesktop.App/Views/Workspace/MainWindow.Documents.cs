using ExcalidrawDesktop.App.Services.Logging;
using ExcalidrawDesktop.App.Services.Documents;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI.StartScreen;

namespace ExcalidrawDesktop.App;

// Native view wiring for this feature; document workflows stay in the controllers.
public sealed partial class MainWindow
{
    private static void TryPostEditorMessage(
        DocumentSession session,
        string message) =>
        session.TryPostEditorMessage(message);

    internal void QueueActivatedFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var canonicalPath = DesktopDocumentPath.Normalize(path);
            if (canonicalPath is not null &&
                !pendingActivatedFiles.Contains(
                    canonicalPath,
                    StringComparer.OrdinalIgnoreCase))
            {
                pendingActivatedFiles.Enqueue(canonicalPath);
            }
        }

        if (isWindowReady)
        {
            _ = DrainActivatedFilesAsync();
        }
    }

    private async Task DrainActivatedFilesAsync()
    {
        if (!isWindowReady || openingActivatedFiles)
        {
            return;
        }

        openingActivatedFiles = true;
        try
        {
            while (pendingActivatedFiles.TryDequeue(out var path))
            {
                await documents.OpenPathInTabAsync(path);
            }
        }
        finally
        {
            openingActivatedFiles = false;
        }
    }

    private async Task RestoreWorkspaceAsync()
    {
        try
        {
            var state = workspaceCoordinator.GetRestoreState(this) ??
                new WorkspaceWindowState(
                    workspaceCoordinator.GetLogicalWindowId(this),
                    null,
                    false,
                    null,
                    Array.Empty<WorkspaceTabState>());

            var initialSession = sessions[0];
            var restoredSessions = new List<DocumentSession>();
            foreach (var savedTab in state.Tabs)
            {
                if (!desktopPreferences.ReopenSavedTabs && !savedTab.WasDirty)
                {
                    continue;
                }

                var path = DesktopDocumentPath.Normalize(savedTab.Path);
                if (path is not null && restoredSessions.Any(session =>
                        DesktopDocumentPath.Equals(
                            session.DocumentService.DocumentPath,
                            path)))
                {
                    continue;
                }

                DocumentSession? target = null;
                try
                {
                    target = restoredSessions.Count == 0
                        ? initialSession
                        : CreateTab(select: false);
                    if (savedTab.WasDirty)
                    {
                        if (savedTab.RecoveryId is not { } recoveryId ||
                            !Guid.TryParseExact(recoveryId, "N", out _))
                        {
                            if (!ReferenceEquals(target, initialSession))
                            {
                                CloseSession(target, discardRecovery: false);
                            }
                            continue;
                        }

                        var content = await recoverySnapshotStore.LoadAsync(recoveryId);
                        if (content is null)
                        {
                            if (!ReferenceEquals(target, initialSession))
                            {
                                CloseSession(target, discardRecovery: false);
                            }
                            continue;
                        }

                        ExcalidrawDocumentValidator.Validate(content);
                        target.RecoveryId = recoveryId;
                        target.RecoveryUpdatedAt = savedTab.RecoveryUpdatedAt;
                        var displayName = savedTab.DisplayName ??
                            (path is null ? DesktopResources.Get("RecoveredDrawingName", "Recovered drawing") : Path.GetFileName(path));
                        await documents.RestoreRecoveryAsync(target, path, displayName, content);
                    }
                    else
                    {
                        if (path is null || !File.Exists(path))
                        {
                            if (!ReferenceEquals(target, initialSession))
                            {
                                CloseSession(target, discardRecovery: false);
                            }
                            continue;
                        }

                        var document = await target.DocumentService.OpenPathAsync(path);
                        target.RecoveryId = savedTab.RecoveryId is { } cleanRecoveryId &&
                            Guid.TryParseExact(cleanRecoveryId, "N", out _)
                                ? cleanRecoveryId
                                : target.RecoveryId;
                        documents.AttachDocumentToSession(target, document, select: false);
                    }
                    restoredSessions.Add(target);
                }
                catch (Exception exception)
                {
                    if (target is not null)
                    {
                        CloseSession(target, discardRecovery: false);
                        if (ReferenceEquals(target, initialSession)) initialSession = sessions[0];
                    }
                    AppLogger.Error($"[MainWindow] Skipped workspace drawing '{path}'", exception);
                    recentFiles.RemoveAll(recent =>
                        DesktopDocumentPath.Equals(recent, path));
                }
            }

            if (restoredSessions.Count > 0)
            {
                var active = restoredSessions.FirstOrDefault(session =>
                    string.Equals(
                        session.RecoveryId,
                        state.ActiveRecoveryId,
                        StringComparison.OrdinalIgnoreCase)) ??
                    restoredSessions[0];
                DocumentTabs.SelectedItem = active.TabItem;
            }

        }
        catch (Exception exception)
        {
            AppLogger.Error("[MainWindow] Workspace restore failed", exception);
        }
        finally
        {
            restoringWorkspace = false;
            workspaceCoordinator.NotifyRecentFilesChanged();
        }
    }

    private void PopulateRecentFilesMenu()
    {
        if (recentFiles.RemoveAll(path => !File.Exists(path)) > 0)
        {
            QueuePersistWorkspace();
            QueueJumpListUpdate();
        }

        RecentFilesMenu.Items.Clear();
        if (recentFiles.Count == 0)
        {
            RecentFilesMenu.Items.Add(new MenuFlyoutItem
            {
                Text = DesktopResources.Get(
                    "RecentFilesEmpty",
                    "No recent drawings"),
                IsEnabled = false,
            });
            return;
        }

        foreach (var path in recentFiles)
        {
            var item = new MenuFlyoutItem { Text = Path.GetFileName(path) };
            ToolTipService.SetToolTip(item, path);
            AutomationProperties.SetHelpText(item, path);
            item.Click += (_, _) => _ = documents.OpenRecentFileAsync(path);
            RecentFilesMenu.Items.Add(item);
        }

        RecentFilesMenu.Items.Add(new MenuFlyoutSeparator());
        var clear = new MenuFlyoutItem
        {
            Text = DesktopResources.Get(
                "ClearRecentDrawings",
                "Clear recent drawings"),
        };
        clear.Click += (_, _) =>
        {
            recentFiles.Clear();
            workspaceCoordinator.NotifyRecentFilesChanged();
        };
        RecentFilesMenu.Items.Add(clear);
    }

    internal void RefreshRecentFiles()
    {
        PopulateRecentFilesMenu();
        QueueJumpListUpdate();
    }

    private void AddRecentFile(string path)
    {
        var canonicalPath = DesktopDocumentPath.Normalize(path);
        if (canonicalPath is null)
        {
            return;
        }

        recentFiles.RemoveAll(recent =>
            !File.Exists(recent) ||
            DesktopDocumentPath.Equals(recent, canonicalPath));
        recentFiles.Insert(0, canonicalPath);
        if (recentFiles.Count > 10)
        {
            recentFiles.RemoveRange(10, recentFiles.Count - 10);
        }
        workspaceCoordinator.NotifyRecentFilesChanged();
    }

    private void QueueJumpListUpdate()
    {
        jumpListUpdateQueued = true;
        if (jumpListUpdateRunning)
        {
            return;
        }

        jumpListUpdateRunning = true;
        _ = UpdateJumpListAsync();
    }

    private async Task UpdateJumpListAsync()
    {
        try
        {
            if (!JumpList.IsSupported())
            {
                jumpListUpdateQueued = false;
                return;
            }

            while (jumpListUpdateQueued)
            {
                jumpListUpdateQueued = false;
                var paths = recentFiles
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToArray();
                var jumpList = await JumpList.LoadCurrentAsync();
                jumpList.SystemGroupKind = JumpListSystemGroupKind.None;
                jumpList.Items.Clear();
                foreach (var path in paths)
                {
                    var item = JumpListItem.CreateWithArguments(
                        DesktopLaunchFile.FormatArguments(path),
                        Path.GetFileName(path));
                    item.Description = path;
                    item.GroupName = "Recent drawings";
                    jumpList.Items.Add(item);
                }

                await jumpList.SaveAsync();
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("[MainWindow] Jump list update failed", exception);
        }
        finally
        {
            jumpListUpdateRunning = false;
            if (jumpListUpdateQueued)
            {
                QueueJumpListUpdate();
            }
        }
    }

    private void OnDirtyChanged(DocumentSession session, bool isDirty)
    {
        if (!sessions.Contains(session))
        {
            return;
        }

        if (!isDirty && session.IsRetrying &&
            session.PendingDocumentLoad?.IsRecovery == true)
        {
            return;
        }

#if DEBUG
        if (session.ForceDirtyForSmoke && !isDirty)
        {
            return;
        }
#endif
        session.IsDirty = isDirty;
        session.DocumentStateVersion++;
        if (!ReferenceEquals(session, ActiveSession))
        {
            session.InactiveSince = DateTimeOffset.UtcNow;
        }
        if (!isDirty)
        {
            session.RecoveryUpdatedAt = null;
            session.RecoveryFailed = false;
            _ = documents.DeleteRecoverySnapshotAsync(session, onlyIfClean: true);
        }
        UpdateTabHeader(session);
        UpdateWindowTitle();
        QueuePersistWorkspace();
    }

    private void OnDocumentCreated(DocumentSession session)
    {
        _ = documents.DeleteRecoverySnapshotAsync(session);
        session.RecoveryId = Guid.NewGuid().ToString("N");
        session.RecoveryUpdatedAt = null;
        session.ExternalFileState = ExternalFileState.None;
        session.DisplayName = NextUntitledName();
        UpdateTabHeader(session);
        UpdateWindowTitle();
        QueuePersistWorkspace();
    }

    private void OnDocumentOpened(DocumentSession session, string fileName)
    {
        session.PendingDocumentLoad = null;
        editorSessions.CompleteRetry(session);
        if (session.IsRestoringFromHibernation)
        {
            session.DisplayName = fileName;
            editorSessions.CompleteHibernationRestore(session);
            UpdateWindowTitle();
            if (session.DocumentService.DocumentPath is { } restoredPath)
            {
                AddRecentFile(restoredPath);
                _ = documents.CheckExternalFileStateAsync(session, showPrompt: false);
            }
            return;
        }

        session.RecoveryUpdatedAt = null;
        _ = documents.DeleteRecoverySnapshotAsync(session, onlyIfClean: true);
        session.ExternalFileState = ExternalFileState.None;
        session.DisplayName = fileName;
        UpdateTabHeader(session);
        UpdateWindowTitle();
        if (session.DocumentService.DocumentPath is { } path)
        {
            documents.WatchExternalFile(session, path);
            AddRecentFile(path);
            _ = documents.CheckExternalFileStateAsync(session, showPrompt: false);
        }
    }

    private void OnDocumentLoadApplied(DocumentSession session, Guid loadId, string fileName, bool isRecovery)
    {
        if (session.PendingDocumentLoad is not { } pending || pending.LoadId != loadId ||
            pending.FileName != fileName || pending.IsRecovery != isRecovery) return;
        if (!isRecovery && !session.DocumentService.ConfirmOpened(fileName, loadId)) return;
        session.PendingPathReservation?.Dispose();
        session.PendingPathReservation = null;
        if (isRecovery)
        {
            OnDocumentRecovered(session);
            workspaceCoordinator.ReleasePreservedRecovery(session.RecoveryId);
        }
        else
        {
            OnDocumentOpened(session, fileName);
        }
    }

    private void OnDocumentSaved(DocumentSession session, string fileName)
    {
        if (!sessions.Contains(session)) return;
        // The saved revision can precede the current editor revision. Only a
        // subsequent clean acknowledgement may delete recovery for this scene.
        session.DisplayName = fileName;
        session.ExternalFileState = ExternalFileState.None;
        _ = documents.RefreshRecoveryBaselineAsync(session);
        if (session.DocumentService.DocumentPath is { } path)
        {
            documents.WatchExternalFile(session, path);
            AddRecentFile(path);
        }
        UpdateTabHeader(session);
        UpdateWindowTitle();
    }

    private void OnDocumentRecovered(DocumentSession session)
    {
        if (!sessions.Contains(session))
        {
            return;
        }

        session.PendingDocumentLoad = null;
        session.IsDirty = true;
        editorSessions.CompleteRetry(session);
        UpdateTabHeader(session);
        UpdateWindowTitle();
#if DEBUG
        if (smoke.VerifyRecoverySmoke)
        {
            recoveryTabsRestored++;
            if (sessions.FirstOrDefault(openSession =>
                    openSession.PendingDocumentLoad is not null) is { } pending)
            {
                DocumentTabs.SelectedItem = pending.TabItem;
            }
            else if (recoveryTabsRestored == sessions.Count)
            {
                Title = "Excalidraw Desktop — Recovery smoke restored";
            }
        }
#endif
        QueuePersistWorkspace();
    }

    private void QueuePersistWorkspace()
    {
        if (restoringWorkspace)
        {
            return;
        }

        workspaceCoordinator.QueuePersistWorkspace();
    }

    private async Task PersistWorkspaceAsync(bool treatDirtyAsClean = false)
    {
        if (restoringWorkspace)
        {
            return;
        }

        try
        {
            await workspaceCoordinator.PersistWorkspaceAsync(
                treatDirtyAsClean ? this : null);
        }
        catch (Exception exception)
        {
            AppLogger.Error("[MainWindow] Workspace persistence failed", exception);
        }
    }

    internal WorkspaceWindowState CaptureWorkspaceState(
        bool treatDirtyAsClean = false,
        bool capturePlacement = true)
    {
        var activeSession = ActiveSession ??
            (lastDocumentSession is not null && sessions.Contains(lastDocumentSession)
                ? lastDocumentSession
                : null);
        WorkspaceWindowBounds? bounds = null;
        var isMaximized = false;
        try
        {
            if (capturePlacement)
            {
                var position = AppWindow.Position;
                var size = AppWindow.Size;
                bounds = new WorkspaceWindowBounds(
                    position.X,
                    position.Y,
                    size.Width,
                    size.Height);
                isMaximized = AppWindow.Presenter is OverlappedPresenter presenter &&
                    presenter.State == OverlappedPresenterState.Maximized;
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("[MainWindow] Window placement capture skipped during close", exception);
        }
        return new WorkspaceWindowState(
            workspaceCoordinator.GetLogicalWindowId(this),
            bounds,
            isMaximized,
            activeSession?.RecoveryId,
            GetOrderedSessions()
                .Where(session =>
                    session.DocumentService.DocumentPath is not null ||
                    (!treatDirtyAsClean && session.IsDirty))
                .Select(session => new WorkspaceTabState(
                    session.DocumentService.DocumentPath,
                    !treatDirtyAsClean && session.IsDirty,
                    session.RecoveryId,
                    session.DisplayName,
                    session.RecoveryUpdatedAt))
                .ToArray());
    }
}
