using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class WorkspaceStateStoreTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ExcalidrawDesktopTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task RoundTripsVersionedWorkspaceMetadata()
    {
        var path = Path.Combine(testDirectory, "workspace.json");
        var store = new WorkspaceStateStore(path);
        var state = new WorkspaceState(
            WorkspaceState.CurrentVersion,
            new[]
            {
                new WorkspaceTabState(@"C:\Drawings\one.excalidraw", false),
                new WorkspaceTabState(@"C:\Drawings\two.excalidraw", true),
            },
            @"C:\Drawings\one.excalidraw",
            new[] { @"C:\Drawings\two.excalidraw" });

        await store.SaveAsync(state);

        var restored = await store.LoadAsync();
        Assert.Equal(state.Version, restored.Version);
        Assert.Equal(state.ActivePath, restored.ActivePath);
        Assert.Equal(state.Tabs, restored.Tabs);
        Assert.Equal(state.RecentFiles, restored.RecentFiles);
    }

    [Fact]
    public async Task MissingCorruptAndUnknownStateStartsEmpty()
    {
        var path = Path.Combine(testDirectory, "workspace.json");
        var store = new WorkspaceStateStore(path);
        Assert.Equal(WorkspaceState.Empty, await store.LoadAsync());

        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(path, "not json");
        Assert.Equal(WorkspaceState.Empty, await store.LoadAsync());

        await File.WriteAllTextAsync(
            path,
            "{\"version\":999,\"tabs\":[],\"recentFiles\":[]}");
        Assert.Equal(WorkspaceState.Empty, await store.LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}
