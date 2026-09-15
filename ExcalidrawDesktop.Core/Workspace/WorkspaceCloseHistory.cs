namespace ExcalidrawDesktop.Core;

/// <summary>Pure workspace transitions, committed atomically by the desktop host.</summary>
public static class WorkspaceCloseHistory
{
    public static MultiWindowWorkspaceState CloseWindow(MultiWindowWorkspaceState state, string windowId)
    {
        var window = state.Windows.Single(window => window.Id == windowId) with { RestoreAllTabs = true };
        if (state.Windows.Count == 1)
            return state with { Windows = [window], LastActiveWindowId = window.Id };
        return Add(state with { Windows = state.Windows.Where(w => w.Id != windowId).ToArray() },
            new(Guid.NewGuid().ToString("N"), true, window, 0, DateTimeOffset.UtcNow));
    }

    public static MultiWindowWorkspaceState CloseTab(MultiWindowWorkspaceState state, string windowId, string recoveryId)
    {
        var window = state.Windows.Single(window => window.Id == windowId);
        var index = window.Tabs.ToList().FindIndex(tab => tab.RecoveryId == recoveryId);
        if (index < 0) throw new InvalidOperationException("The drawing is no longer open.");
        var remaining = window.Tabs.Where(tab => tab.RecoveryId != recoveryId).ToArray();
        var active = window.ActiveRecoveryId == recoveryId
            ? remaining.ElementAtOrDefault(Math.Min(index, remaining.Length - 1))?.RecoveryId
            : window.ActiveRecoveryId;
        return Add(state with
        {
            Windows = state.Windows.Select(w => w.Id == windowId
                ? w with { Tabs = remaining, ActiveRecoveryId = active } : w).ToArray(),
        }, new(Guid.NewGuid().ToString("N"), false,
            window with { Tabs = [window.Tabs[index]], ActiveRecoveryId = recoveryId, RestoreAllTabs = true },
            index, DateTimeOffset.UtcNow));
    }

    public static MultiWindowWorkspaceState Exit(MultiWindowWorkspaceState state) => state with
    {
        Windows = state.Windows.Select(window => window with { RestoreAllTabs = true }).ToArray(),
    };

    private static MultiWindowWorkspaceState Add(MultiWindowWorkspaceState state, ClosedWorkspaceItem item)
    {
        // Keep the newest 20 clean entries and every entry containing an unsaved
        // drawing. A size limit must never delete the only copy of a draft.
        var cleanCount = 0;
        return state with
        {
            ClosedItems = new[] { item }.Concat(state.ClosedItems)
                .Where(entry => entry.Window.Tabs.Any(tab => tab.WasDirty) || ++cleanCount <= 20).ToArray(),
        };
    }

    public static IEnumerable<string> RecoveryIds(MultiWindowWorkspaceState state) =>
        state.Windows.Concat(state.ClosedItems.Select(item => item.Window))
            .SelectMany(window => window.Tabs).Where(tab => tab.WasDirty)
            .Select(tab => tab.RecoveryId).OfType<string>();
}
