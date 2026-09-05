using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App.Services;

internal sealed class ApplicationWorkspaceCoordinator
{
    private readonly List<MainWindow> windows = [];
    private readonly Dictionary<MainWindow, string> logicalWindowIds = [];
    private readonly Dictionary<MainWindow, WorkspaceWindowState> restoreStates = [];
    private readonly MultiWindowWorkspaceStateStore workspaceStateStore;
    private readonly RecoverySnapshotStore recoverySnapshotStore;
    private readonly DesktopSettingsStore desktopSettingsStore = new();
    private readonly SemaphoreSlim persistenceGate = new(1, 1);
    private CancellationTokenSource? queuedPersistence;
    private MainWindow? mostRecentlyActiveWindow;
    private string? restoredLastActiveWindowId;
    private bool isExiting;
    private bool startupRecoveryPruned;
    private Dictionary<string, WorkspaceWindowState>? exitWorkspace;

    public ApplicationWorkspaceCoordinator(
        string? workspaceStatePath = null,
        DesktopPreferences? startupPreferences = null)
    {
        WorkspaceStatePath = workspaceStatePath ?? DesktopPaths.WorkspaceStatePath;
        workspaceStateStore = new MultiWindowWorkspaceStateStore(WorkspaceStatePath);
        recoverySnapshotStore = new RecoverySnapshotStore(Path.Combine(
            Path.GetDirectoryName(WorkspaceStatePath)!,
            $"{Path.GetFileNameWithoutExtension(WorkspaceStatePath)}.recovery"));
        Preferences = startupPreferences ?? desktopSettingsStore.Load();
        StartupLanguagePreference = Preferences.Language;
        EffectiveLanguage = DesktopLanguageStartup.ResolveEffective(
            Preferences.Language);
    }

    public IReadOnlyList<MainWindow> Windows => windows;
    public List<string> RecentFiles { get; } = [];
    public string WorkspaceStatePath { get; }
    public IReadOnlyList<WorkspaceWindowState> RestoredWindows { get; private set; } = [];
    public DesktopPreferences Preferences { get; private set; }
    public DesktopLanguage EffectiveLanguage { get; }
    public string StartupLanguagePreference { get; }

    public void UpdatePreferences(DesktopPreferences preferences)
    {
        preferences = preferences with
        {
            Language = DesktopLanguages.NormalizePreference(preferences.Language),
        };
        Preferences = preferences;
        desktopSettingsStore.Save(preferences);
        DiagnosticLogService.Info("settings.changed", new
        {
            theme = preferences.Theme.ToString(),
            preferences.ReopenSavedTabs,
            preferences.SaveDirtyDrawingsOnClose,
            preferences.SuspendInactiveTabs,
            preferences.UnloadInactiveTabs,
            language = preferences.Language,
        });
        foreach (var window in windows.ToArray())
        {
            window.ApplySharedPreferences(preferences);
        }
    }

    public async Task InitializeAsync()
    {
        var state = await workspaceStateStore.LoadAsync();
        RecentFiles.Clear();
        RecentFiles.AddRange(state.RecentFiles
            .Select(DesktopDocumentPath.Normalize)
            .OfType<string>()
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10));
        RestoredWindows = state.Windows;
        restoredLastActiveWindowId = state.LastActiveWindowId;
    }

    public MainWindow CreateWindow(
        bool activate = true,
        bool createInitialTab = true,
        WorkspaceWindowState? restoreState = null)
    {
        if (isExiting)
        {
            throw new InvalidOperationException(
                "A new window cannot be created while the application is exiting.");
        }
        var window = new MainWindow(
            this,
            workspaceStatePath: WorkspaceStatePath,
            restoreWorkspace: restoreState is not null,
            createInitialTab: createInitialTab);
        RegisterWindow(window, restoreState);
        if (activate)
        {
            window.Activate();
        }
        return window;
    }

    public void RegisterWindow(
        MainWindow window,
        WorkspaceWindowState? restoreState = null)
    {
        if (!windows.Contains(window))
        {
            windows.Add(window);
        }

        var logicalId = restoreState?.Id;
        if (string.IsNullOrWhiteSpace(logicalId) || logicalWindowIds.Values.Contains(
                logicalId,
                StringComparer.OrdinalIgnoreCase))
        {
            logicalId = Guid.NewGuid().ToString("N");
        }
        logicalWindowIds[window] = logicalId;
        if (restoreState is not null)
        {
            restoreStates[window] = restoreState;
        }
        // Registration also happens for the hidden placeholder used by a tab
        // tear-out. Only an activated window should become the activation
        // target for subsequent app-instance redirects.
        mostRecentlyActiveWindow ??= window;
        DiagnosticLogService.Info("window.registered", new
        {
            windowId = logicalId,
            windowCount = windows.Count,
            restored = restoreState is not null,
        });
    }

    public WorkspaceWindowState? GetRestoreState(MainWindow window) =>
        restoreStates.TryGetValue(window, out var state) ? state : null;

    public string GetLogicalWindowId(MainWindow window) =>
        logicalWindowIds.TryGetValue(window, out var id)
            ? id
            : throw new InvalidOperationException("The window is not registered.");

    public bool IsRestoredLastActiveWindow(MainWindow window) =>
        string.Equals(GetLogicalWindowId(window), restoredLastActiveWindowId,
            StringComparison.OrdinalIgnoreCase);

    public void NotifyWindowActivated(MainWindow window)
    {
        if (windows.Contains(window))
        {
            if (ReferenceEquals(mostRecentlyActiveWindow, window))
            {
                return;
            }
            mostRecentlyActiveWindow = window;
            DiagnosticLogService.Info("window.activated", new
            {
                windowId = GetLogicalWindowId(window),
                windowCount = windows.Count,
                tabCount = window.OpenSessions.Count,
            });
            if (window.IsReadyForActivation)
            {
                QueuePersistWorkspace();
            }
        }
    }

    public void ActivateMostRecentWindow()
    {
        var target = mostRecentlyActiveWindow?.IsReadyForActivation == true
            ? mostRecentlyActiveWindow
            : windows.LastOrDefault(window => window.IsReadyForActivation);
        target?.Activate();
    }

    public void UnregisterWindow(MainWindow window)
    {
        logicalWindowIds.TryGetValue(window, out var removedWindowId);
        windows.Remove(window);
        logicalWindowIds.Remove(window);
        restoreStates.Remove(window);
        DiagnosticLogService.Info("window.unregistered", new
        {
            windowId = removedWindowId,
            windowCount = windows.Count,
            exiting = isExiting,
        });
        if (ReferenceEquals(mostRecentlyActiveWindow, window))
        {
            mostRecentlyActiveWindow = windows.LastOrDefault();
        }
        if (windows.Count > 0 && !isExiting)
        {
            QueuePersistWorkspace();
        }
        else if (windows.Count == 0 && !isExiting)
        {
            DiagnosticLogService.Info("application.exiting", new
            {
                reason = "last_window_closed",
            });
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }

    public void NotifyRecentFilesChanged()
    {
        foreach (var window in windows)
        {
            window.RefreshRecentFiles();
        }
        QueuePersistWorkspace();
    }

    public void RecordWindowDiscarded(MainWindow window)
    {
        if (exitWorkspace is not null && logicalWindowIds.TryGetValue(window, out var id))
        {
            var state = window.CaptureWorkspaceState(treatDirtyAsClean: true);
            exitWorkspace[id] = state;
        }
    }

    public void QueuePersistWorkspace()
    {
        queuedPersistence?.Cancel();
        queuedPersistence?.Dispose();
        queuedPersistence = new CancellationTokenSource();
        _ = PersistAfterDelayAsync(queuedPersistence.Token);
    }

    private async Task PersistAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(150, cancellationToken);
            await PersistWorkspaceAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("workspace.persistence_failed", exception);
            System.Diagnostics.Debug.WriteLine(
                $"Workspace persistence failed: {exception}");
        }
    }

    public async Task PersistWorkspaceAsync(
        MainWindow? treatDirtyAsCleanInWindow = null)
    {
        var liveSnapshots = windows.Select(window =>
            CaptureWindowForPersistence(
                window,
                ReferenceEquals(window, treatDirtyAsCleanInWindow)))
            .OfType<WorkspaceWindowState>()
            .ToArray();
        if (isExiting && exitWorkspace is not null)
        {
            foreach (var liveSnapshot in liveSnapshots)
            {
                exitWorkspace[liveSnapshot.Id] = liveSnapshot;
            }
        }
        var snapshotWindows = isExiting && exitWorkspace is not null
            ? exitWorkspace.Values.ToArray()
            : liveSnapshots;
        var lastActiveId = mostRecentlyActiveWindow is not null &&
            logicalWindowIds.TryGetValue(mostRecentlyActiveWindow, out var id) &&
            snapshotWindows.Any(window => string.Equals(
                window.Id,
                id,
                StringComparison.OrdinalIgnoreCase))
                ? id
                : snapshotWindows.FirstOrDefault()?.Id;
        var state = new MultiWindowWorkspaceState(
            MultiWindowWorkspaceState.CurrentVersion,
            RecentFiles.ToArray(),
            lastActiveId,
            snapshotWindows);

        await persistenceGate.WaitAsync();
        try
        {
            await workspaceStateStore.SaveAsync(state);
            DiagnosticLogService.Info("workspace.persisted", new
            {
                windowCount = snapshotWindows.Length,
                recentFileCount = RecentFiles.Count,
                exiting = isExiting,
            });
        }
        finally
        {
            persistenceGate.Release();
        }
    }

    private WorkspaceWindowState? CaptureWindowForPersistence(
        MainWindow window,
        bool treatDirtyAsClean = false)
    {
        if (window.IsReadyForActivation)
        {
            return window.CaptureWorkspaceState(treatDirtyAsClean);
        }

        // A restored window can be persisted from its saved state until it
        // finishes loading. A tear-out destination that already owns a tab
        // must also be captured, but its empty placeholder must not become a
        // blank window on the next launch.
        return restoreStates.GetValueOrDefault(window) ??
            (window.OpenSessions.Count > 0
                ? window.CaptureWorkspaceState(
                    treatDirtyAsClean,
                    capturePlacement: false)
                : null);
    }

    public Task PruneRecoverySnapshotsAsync(MainWindow? excludedWindow = null) =>
        recoverySnapshotStore.PruneExceptAsync(
            windows.Where(window => !ReferenceEquals(window, excludedWindow))
                .SelectMany(window => window.OpenSessions)
                .Where(session => session.IsDirty)
                .Select(session => session.RecoveryId));

    public async Task NotifyWindowReadyAsync(MainWindow window)
    {
        if (!windows.Contains(window))
        {
            return;
        }

        QueuePersistWorkspace();
        if (startupRecoveryPruned || windows.Any(candidate =>
                !candidate.IsReadyForActivation))
        {
            return;
        }

        startupRecoveryPruned = true;
        DiagnosticLogService.Info("window.ready", new
        {
            windowId = GetLogicalWindowId(window),
            windowCount = windows.Count,
            tabCount = window.OpenSessions.Count,
        });
        try
        {
            await PruneRecoverySnapshotsAsync();
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("recovery.prune_failed", exception);
            System.Diagnostics.Debug.WriteLine(
                $"Startup recovery snapshot cleanup failed: {exception}");
        }
    }

    public bool IsPathOwnedByAnotherSession(
        DocumentSession requester,
        string path) =>
        FindSessionByPath(path) is { Session: var owner } &&
        !ReferenceEquals(owner, requester);

    public (MainWindow Window, DocumentSession Session)? FindSessionByPath(
        string? path)
    {
        var canonicalPath = DesktopDocumentPath.Normalize(path);
        if (canonicalPath is null)
        {
            return null;
        }
        foreach (var window in windows)
        {
            var session = window.OpenSessions.FirstOrDefault(candidate =>
                DesktopDocumentPath.Equals(
                    candidate.DocumentService.DocumentPath,
                    canonicalPath));
            if (session is not null)
            {
                return (window, session);
            }
        }
        return null;
    }

    public void ActivateSession(MainWindow window, DocumentSession session)
    {
        if (!windows.Contains(window) || !window.OpenSessions.Contains(session))
        {
            return;
        }
        window.SelectSession(session);
        window.Activate();
        mostRecentlyActiveWindow = window;
    }

    public (MainWindow Window, DocumentSession Session)? FindSessionByTab(
        Microsoft.UI.Xaml.Controls.TabViewItem tab)
    {
        foreach (var window in windows)
        {
            var session = window.OpenSessions.FirstOrDefault(candidate =>
                ReferenceEquals(candidate.TabItem, tab));
            if (session is not null)
            {
                return (window, session);
            }
        }
        return null;
    }

    public void QueueActivatedFiles(IEnumerable<string> paths)
    {
        if (isExiting)
        {
            return;
        }
        foreach (var path in paths)
        {
            var canonicalPath = DesktopDocumentPath.Normalize(path);
            if (canonicalPath is null)
            {
                continue;
            }
            if (FindSessionByPath(canonicalPath) is { } existing)
            {
                ActivateSession(existing.Window, existing.Session);
                continue;
            }
            var target = mostRecentlyActiveWindow is { IsReadyForActivation: true }
                ? mostRecentlyActiveWindow
                : windows.LastOrDefault(window => window.IsReadyForActivation) ??
                    windows.LastOrDefault() ?? CreateWindow(activate: false);
            target.QueueActivatedFiles([canonicalPath]);
            target.Activate();
            mostRecentlyActiveWindow = target;
        }
    }

    public bool MoveSessionToNewWindow(MainWindow source, DocumentSession session)
    {
        if (!windows.Contains(source))
        {
            return false;
        }
        var destination = CreateWindow(activate: true, createInitialTab: false);
        if (MoveSession(source, session, destination))
        {
            return true;
        }
        destination.CloseIfEmptyAfterMove();
        return false;
    }

    public bool MoveSession(
        MainWindow source,
        DocumentSession session,
        MainWindow destination,
        int? index = null)
    {
        if (isExiting || ReferenceEquals(source, destination) ||
            !windows.Contains(source) || !windows.Contains(destination))
        {
            return false;
        }
        var transfer = source.DetachSessionForMove(session);
        if (transfer is null)
        {
            return false;
        }
        try
        {
            destination.AttachMovedSession(transfer, index);
            destination.Activate();
            mostRecentlyActiveWindow = destination;
            source.CloseIfEmptyAfterMove();
            QueuePersistWorkspace();
            return true;
        }
        catch
        {
            session.IsMoving = false;
            var rollback = destination.DetachSessionForMove(session) ?? transfer;
            source.AttachMovedSession(rollback, transfer.SourceIndex);
            destination.CloseIfEmptyAfterMove();
            source.Activate();
            throw;
        }
    }

    public async Task RequestExitAsync()
    {
        if (isExiting)
        {
            return;
        }

        isExiting = true;
        DiagnosticLogService.Info("application.exit_requested", new
        {
            windowCount = windows.Count,
            dirtyTabCount = windows.Sum(window =>
                window.OpenSessions.Count(session => session.IsDirty)),
        });
        queuedPersistence?.Cancel();
        queuedPersistence?.Dispose();
        queuedPersistence = null;
        exitWorkspace = windows
            .Select(window => CaptureWindowForPersistence(window))
            .OfType<WorkspaceWindowState>()
            .ToDictionary(window => window.Id, StringComparer.OrdinalIgnoreCase);
        try
        {
            await PersistWorkspaceAsync();
            foreach (var window in windows.ToArray())
            {
                if (!windows.Contains(window))
                {
                    continue;
                }

                if (!await window.ActivateForExitAsync())
                {
                    DiagnosticLogService.Info("application.exit_cancelled", new
                    {
                        reason = "activation_failed",
                        windowId = GetLogicalWindowId(window),
                    });
                    return;
                }
                if (!await window.RequestCloseAsync())
                {
                    DiagnosticLogService.Info("application.exit_cancelled", new
                    {
                        reason = "window_close_cancelled",
                        windowId = GetLogicalWindowId(window),
                    });
                    return;
                }
            }
            await PersistWorkspaceAsync();
            DiagnosticLogService.Info("application.exit_completed");
        }
        finally
        {
            isExiting = false;
            exitWorkspace = null;
            if (windows.Count > 0)
            {
                QueuePersistWorkspace();
            }
        }
    }
}
