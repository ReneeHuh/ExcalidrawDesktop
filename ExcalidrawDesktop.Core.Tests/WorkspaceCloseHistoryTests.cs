using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class WorkspaceCloseHistoryTests
{
    private static WorkspaceTabState Tab(bool dirty = true) => new(null, dirty, Guid.NewGuid().ToString("N"), "Untitled");
    private static WorkspaceWindowState Window(string id, params WorkspaceTabState[] tabs) =>
        new(id, new(10, 20, 900, 700), true, tabs.LastOrDefault()?.RecoveryId, tabs);
    private static MultiWindowWorkspaceState State(params WorkspaceWindowState[] windows) =>
        new(MultiWindowWorkspaceState.CurrentVersion, [], windows.LastOrDefault()?.Id, windows);

    [Fact]
    public void CloseBThenExitAKeepsBInHistoryAndOnlyARestarts()
    {
        var a = Window("a", Tab());
        var b = Window("b", Tab(), Tab(false));
        var result = WorkspaceCloseHistory.Exit(WorkspaceCloseHistory.CloseWindow(State(a, b), "b"));
        Assert.Equal("a", Assert.Single(result.Windows).Id);
        Assert.True(result.Windows[0].RestoreAllTabs);
        var closed = Assert.Single(result.ClosedItems);
        Assert.True(closed.IsWindow);
        Assert.Equal(b.Tabs, closed.Window.Tabs);
        Assert.Equal(b.Bounds, closed.Window.Bounds);
        Assert.Equal(b.ActiveRecoveryId, closed.Window.ActiveRecoveryId);
        Assert.Contains(b.Tabs[0].RecoveryId, WorkspaceCloseHistory.RecoveryIds(result));
    }

    [Fact]
    public void TabAndWindowClosuresShareNewestFirstOrderWithoutDuplicateMembers()
    {
        var first = Tab(); var second = Tab();
        var result = WorkspaceCloseHistory.CloseWindow(State(Window("a", first, second), Window("b", Tab())), "b");
        result = WorkspaceCloseHistory.CloseTab(result, "a", second.RecoveryId!);
        Assert.Equal(2, result.ClosedItems.Count);
        Assert.False(result.ClosedItems[0].IsWindow);
        Assert.Equal(1, result.ClosedItems[0].TabIndex);
        Assert.Equal(second, Assert.Single(result.ClosedItems[0].Window.Tabs));
        Assert.True(result.ClosedItems[1].IsWindow);
        Assert.Equal(first, Assert.Single(Assert.Single(result.Windows).Tabs));
    }

    [Fact]
    public void FinalWindowRestartsWithoutAnExtraHistoryEntry()
    {
        var blank = Tab(false);
        var result = WorkspaceCloseHistory.CloseWindow(State(Window("a", blank)), "a");
        Assert.Empty(result.ClosedItems);
        Assert.Equal(blank, Assert.Single(Assert.Single(result.Windows).Tabs));
        Assert.True(result.Windows[0].RestoreAllTabs);
    }

    [Fact]
    public void ClosingLastTabRecordsItsDraftAndAllowsBlankReplacement()
    {
        var draft = Tab();
        var result = WorkspaceCloseHistory.CloseTab(State(Window("a", draft)), "a", draft.RecoveryId!);
        Assert.Empty(Assert.Single(result.Windows).Tabs);
        Assert.Equal(draft, Assert.Single(Assert.Single(result.ClosedItems).Window.Tabs));
    }

    [Fact]
    public void CleanHistoryLimitNeverEvictsUnsavedDrafts()
    {
        var result = State(Window("a", Tab()));
        var retained = new List<string>();
        for (var index = 0; index < 60; index++)
        {
            var tab = Tab(index % 2 == 0);
            if (tab.WasDirty) retained.Add(tab.RecoveryId!);
            result = result with { Windows = [Window("a", tab)] };
            result = WorkspaceCloseHistory.CloseTab(result, "a", tab.RecoveryId!);
        }
        Assert.Equal(50, result.ClosedItems.Count);
        Assert.Equal(retained.Order(), WorkspaceCloseHistory.RecoveryIds(result).Order());
    }

    [Fact]
    public async Task HistoryDraftsSurvivePersistenceAndPruning()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CloseHistory-{Guid.NewGuid():N}");
        try
        {
            var draft = Tab();
            var state = WorkspaceCloseHistory.CloseTab(State(Window("a", draft)), "a", draft.RecoveryId!);
            var store = new MultiWindowWorkspaceStateStore(Path.Combine(directory, "state.json"));
            var recovery = new RecoverySnapshotStore(Path.Combine(directory, "drafts"));
            const string content = "{\"type\":\"excalidraw\",\"version\":2,\"elements\":[],\"appState\":{},\"files\":{}}";
            await recovery.SaveAsync(draft.RecoveryId!, content);
            await store.SaveAsync(state);
            var restored = await store.LoadAsync();
            await recovery.PruneExceptAsync(() => WorkspaceCloseHistory.RecoveryIds(restored));
            Assert.Equal(content, await recovery.LoadAsync(draft.RecoveryId!));
            Assert.Equal(draft, Assert.Single(Assert.Single(restored.ClosedItems).Window.Tabs));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MoreThanOneHundredDraftTabsAreNeverTruncated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"CloseHistory-{Guid.NewGuid():N}.json");
        try
        {
            var tabs = Enumerable.Range(0, 125).Select(_ => Tab()).ToArray();
            var store = new MultiWindowWorkspaceStateStore(path);
            await store.SaveAsync(State(Window("a", tabs)));
            var restored = await store.LoadAsync();
            Assert.Equal(MultiWindowWorkspaceStateStore.LoadStatus.Loaded, store.LastLoadStatus);
            Assert.Equal(tabs, Assert.Single(restored.Windows).Tabs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task VersionThreeMigratesWithoutInventingHistoryOrForcingSavedTabRestore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"CloseHistory-{Guid.NewGuid():N}.json");
        try
        {
            var tab = Tab();
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(new
            {
                Version = 3, RecentFiles = Array.Empty<string>(), LastActiveWindowId = "a",
                Windows = new[] { new { Id = "a", Tabs = new[] { tab }, ActiveRecoveryId = tab.RecoveryId } },
            }));
            var store = new MultiWindowWorkspaceStateStore(path);
            var state = await store.LoadAsync();
            Assert.Equal(MultiWindowWorkspaceState.CurrentVersion, state.Version);
            Assert.Empty(state.ClosedItems);
            Assert.False(Assert.Single(state.Windows).RestoreAllTabs);
            Assert.Equal(tab, Assert.Single(state.Windows[0].Tabs));
        }
        finally { File.Delete(path); }
    }
}
