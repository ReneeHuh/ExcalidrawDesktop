using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class MultiWindowWorkspaceStateStoreTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ExcalidrawDesktopMultiWindowTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task RoundTripsAggregateWindowState()
    {
        var path = Path.Combine(testDirectory, "workspace.json");
        var store = new MultiWindowWorkspaceStateStore(path);
        var state = new MultiWindowWorkspaceState(
            MultiWindowWorkspaceState.CurrentVersion,
            new[] { @"C:\Drawings\recent.excalidraw" },
            "window-b",
            new[]
            {
                new WorkspaceWindowState(
                    "window-a",
                    new WorkspaceWindowBounds(20, 30, 1200, 800),
                    false,
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    new[]
                    {
                        new WorkspaceTabState(
                            @"C:\Drawings\one.excalidraw",
                            false,
                            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                            "one.excalidraw"),
                    }),
                new WorkspaceWindowState(
                    "window-b",
                    new WorkspaceWindowBounds(80, 90, 900, 700),
                    true,
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    new[]
                    {
                        new WorkspaceTabState(
                            null,
                            true,
                            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                            "Recovered drawing"),
                    }),
            });

        await store.SaveAsync(state);

        var restored = await store.LoadAsync();
        Assert.Equal(state.Version, restored.Version);
        Assert.Equal(state.LastActiveWindowId, restored.LastActiveWindowId);
        Assert.Equal(state.RecentFiles, restored.RecentFiles);
        Assert.Equal(state.Windows.Count, restored.Windows.Count);
        for (var index = 0; index < state.Windows.Count; index++)
        {
            Assert.Equal(state.Windows[index].Id, restored.Windows[index].Id);
            Assert.Equal(state.Windows[index].Bounds, restored.Windows[index].Bounds);
            Assert.Equal(
                state.Windows[index].IsMaximized,
                restored.Windows[index].IsMaximized);
            Assert.Equal(
                state.Windows[index].ActiveRecoveryId,
                restored.Windows[index].ActiveRecoveryId);
            Assert.Equal(state.Windows[index].Tabs, restored.Windows[index].Tabs);
        }
    }

    [Fact]
    public async Task MigratesVersionTwoIntoOneLogicalWindow()
    {
        var path = Path.Combine(testDirectory, "workspace.json");
        var legacyStore = new WorkspaceStateStore(path);
        var recoveryId = Guid.NewGuid().ToString("N");
        await legacyStore.SaveAsync(new WorkspaceState(
            WorkspaceState.CurrentVersion,
            new[]
            {
                new WorkspaceTabState(
                    @"C:\Drawings\one.excalidraw",
                    false,
                    recoveryId,
                    "one.excalidraw"),
            },
            @"C:\Drawings\one.excalidraw",
            new[] { @"C:\Drawings\one.excalidraw" },
            recoveryId));

        var migrated = await new MultiWindowWorkspaceStateStore(path).LoadAsync();

        Assert.Equal(MultiWindowWorkspaceState.CurrentVersion, migrated.Version);
        var window = Assert.Single(migrated.Windows);
        Assert.Equal("main", window.Id);
        Assert.Equal(recoveryId, window.ActiveRecoveryId);
        Assert.Equal(recoveryId, Assert.Single(window.Tabs).RecoveryId);
    }

    [Fact]
    public async Task RejectsDuplicateWindowIdsAndUnknownVersions()
    {
        Directory.CreateDirectory(testDirectory);
        var path = Path.Combine(testDirectory, "workspace.json");
        await File.WriteAllTextAsync(
            path,
            "{\"version\":3,\"recentFiles\":[],\"windows\":[{\"id\":\"same\",\"tabs\":[]},{\"id\":\"SAME\",\"tabs\":[]}]}" );
        var store = new MultiWindowWorkspaceStateStore(path);
        Assert.Equal(MultiWindowWorkspaceState.Empty, await store.LoadAsync());

        await File.WriteAllTextAsync(
            path,
            "{\"version\":99,\"recentFiles\":[],\"windows\":[]}" );
        Assert.Equal(MultiWindowWorkspaceState.Empty, await store.LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}
