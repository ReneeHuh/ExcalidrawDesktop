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
    public async Task SaveAsync_DoesNotRewriteUnchangedState()
    {
        var path = Path.Combine(testDirectory, "unchanged.json");
        var store = new MultiWindowWorkspaceStateStore(path);
        var state = new MultiWindowWorkspaceState(
            MultiWindowWorkspaceState.CurrentVersion,
            Array.Empty<string>(),
            "main",
            new[]
            {
                new WorkspaceWindowState(
                    "main",
                    null,
                    false,
                    null,
                    Array.Empty<WorkspaceTabState>()),
            });

        await store.SaveAsync(state);
        store = new MultiWindowWorkspaceStateStore(path);
        await store.LoadAsync();
        var marker = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, marker);

        await store.SaveAsync(state);

        Assert.Equal(marker, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task MigratesVersionTwoIntoOneLogicalWindow()
    {
        var path = Path.Combine(testDirectory, "workspace.json");
        var recoveryId = Guid.NewGuid().ToString("N");
        // A legacy single-window file as written by the pre-multi-window store.
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(
            path,
            System.Text.Json.JsonSerializer.Serialize(new WorkspaceState(
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
                recoveryId)));

        var migrated = await new MultiWindowWorkspaceStateStore(path).LoadAsync();

        Assert.Equal(MultiWindowWorkspaceState.CurrentVersion, migrated.Version);
        var window = Assert.Single(migrated.Windows);
        Assert.Equal("main", window.Id);
        Assert.Equal(recoveryId, window.ActiveRecoveryId);
        Assert.Equal(recoveryId, Assert.Single(window.Tabs).RecoveryId);
    }

    [Fact]
    public async Task ReloadingADamagedFileDoesNotLeaveAnOutdatedWriteCache()
    {
        var path = Path.Combine(testDirectory, "cache.json");
        var store = new MultiWindowWorkspaceStateStore(path);
        await store.SaveAsync(MultiWindowWorkspaceState.Empty);
        await File.WriteAllTextAsync(path, "[]");
        Assert.Equal(MultiWindowWorkspaceState.Empty, await store.LoadAsync());
        await store.SaveAsync(MultiWindowWorkspaceState.Empty);
        Assert.Contains("Windows", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"version\":\"3\"}")]
    [InlineData("{\"version\":null}")]
    [InlineData("{\"version\":true}")]
    [InlineData("{\"version\":2147483648}")]
    [InlineData("{\"version\":3,\"recentFiles\":[],\"windows\":[null]}")]
    [InlineData("{\"version\":3,\"recentFiles\":[],\"windows\":[{\"id\":\"main\",\"tabs\":[null]}]}")]
    [InlineData("{\"version\":3,\"recentFiles\":[null],\"windows\":[]}")]
    [InlineData("{\"version\":3,\"recentFiles\":[],\"windows\":[{\"id\":\"main\",\"tabs\":null}]}")]
    [InlineData("{\"version\":2,\"recentFiles\":[],\"tabs\":[null]}")]
    [InlineData("{\"version\":1,\"recentFiles\":[null],\"tabs\":[]}")]
    public async Task DamagedWorkspaceDataFallsBackToAnEmptyWorkspace(string content)
    {
        Directory.CreateDirectory(testDirectory);
        var path = Path.Combine(testDirectory, "damaged.json");
        await File.WriteAllTextAsync(path, content);
        Assert.Equal(MultiWindowWorkspaceState.Empty,
            await new MultiWindowWorkspaceStateStore(path).LoadAsync());
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

    [Fact]
    public async Task SalvagesSafeEntriesAndDeduplicatesRecoveryIds()
    {
        Directory.CreateDirectory(testDirectory);
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(testDirectory, "salvage.json");
        await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(
            new MultiWindowWorkspaceState(3, new[] { "bad\0path", "recent.excalidraw" }, "a",
                new[] { new WorkspaceWindowState("a", null, false, id, new[] {
                    new WorkspaceTabState("bad\0path", false, id),
                    new WorkspaceTabState(null, false, id),
                    new WorkspaceTabState(null, true, "invalid") }) })));
        var store = new MultiWindowWorkspaceStateStore(path);
        var state = await store.LoadAsync();
        var window = Assert.Single(state.Windows);
        Assert.Equal(2, window.Tabs.Count);
        Assert.Equal(MultiWindowWorkspaceStateStore.LoadStatus.Corrupt, store.LastLoadStatus);
        Assert.DoesNotContain("bad\0path", state.RecentFiles);
    }

    [Fact]
    public async Task ClassifiesMissingAndUnsupportedWorkspaceWithoutThrowing()
    {
        var missing = new MultiWindowWorkspaceStateStore(Path.Combine(testDirectory, "missing.json"));
        Assert.Equal(MultiWindowWorkspaceState.Empty, await missing.LoadAsync());
        Assert.Equal(MultiWindowWorkspaceStateStore.LoadStatus.Missing, missing.LastLoadStatus);
        Directory.CreateDirectory(testDirectory);
        var path = Path.Combine(testDirectory, "new.json");
        await File.WriteAllTextAsync(path, "{\"version\":99,\"windows\":[]}");
        var unsupported = new MultiWindowWorkspaceStateStore(path);
        Assert.Equal(MultiWindowWorkspaceState.Empty, await unsupported.LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}
