using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class LibraryStoreTests : IDisposable
{
    [Fact]
    public async Task CorruptionAfterLoadRequiresRepairWithoutLosingEitherVersion()
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "library.json");
        var store = new LibraryStore(path);
        var initial = await store.LoadAsync();
        await File.WriteAllTextAsync(path, "[");
        const string desired = "[{\"id\":\"local\"}]";
        await Assert.ThrowsAsync<LibraryCorruptException>(() => store.SaveAsync(desired, initial.Revision));
        Assert.Equal("[", await File.ReadAllTextAsync(path));
        var corrupt = await store.LoadAsync();
        var repaired = await store.RepairAsync(corrupt.Revision);
        await store.SaveAsync(desired, repaired.Revision);
        Assert.Equal(desired, (await store.LoadAsync()).Content);
        Assert.Equal("[", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(dir, "*.bak"))));
    }

    [Fact]
    public async Task InvalidIncomingContentIsNotReportedAsDiskCorruption()
    {
        var store = new LibraryStore(Path.Combine(dir, "library.json"));
        var initial = await store.LoadAsync();
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => store.SaveAsync("[", initial.Revision));
        Assert.Equal(initial, await store.LoadAsync());
    }

    private readonly string dir = Path.Combine(Path.GetTempPath(), "ExcalidrawLibrary-" + Guid.NewGuid().ToString("N"));
    [Fact] public async Task PersistsAndReloadsWithRevision()
    { var store = new LibraryStore(Path.Combine(dir, "library.json")); var initial = await store.LoadAsync(); var saved = await store.SaveAsync("[{\"id\":1}]", initial.Revision); var loaded = await store.LoadAsync(); Assert.Equal(saved.Revision, loaded.Revision); Assert.Equal(saved.Content, loaded.Content); }
    [Fact] public async Task RejectsStaleRevisionWithoutOverwrite()
    { var store = new LibraryStore(Path.Combine(dir, "library.json")); var first = await store.LoadAsync(); await store.SaveAsync("[1]", first.Revision); await Assert.ThrowsAsync<LibraryConflictException>(() => store.SaveAsync("[2]", first.Revision)); Assert.Equal("[1]", (await store.LoadAsync()).Content); }
    public void Dispose() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

    [Fact]
    public async Task PreservesExistingBomEncodedLibrarySupport()
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "library.json");
        await File.WriteAllTextAsync(path, "[{\"id\":\"existing\"}]", System.Text.Encoding.Unicode);
        var store = new LibraryStore(path);
        var loaded = await store.LoadAsync();
        Assert.Equal("loaded", loaded.Status);
        await store.SaveAsync("[]", loaded.Revision);
        Assert.Equal("[]", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task RepairPreservesDamagedBytesAndAllowsSaving()
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "library.json");
        byte[] damaged = [0xef, 0xbb, 0xbf, (byte)'['];
        await File.WriteAllBytesAsync(path, damaged);
        var store = new LibraryStore(path);
        var loaded = await store.LoadAsync();
        Assert.Equal("corrupt", loaded.Status);
        Assert.Equal(damaged, await File.ReadAllBytesAsync(path));
        var repaired = await store.RepairAsync(loaded.Revision);
        Assert.Equal(damaged, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(dir, "*.bak"))));
        await store.SaveAsync("[{\"id\":\"restored\"}]", repaired.Revision);
        Assert.Equal("[{\"id\":\"restored\"}]", (await store.LoadAsync()).Content);
    }

    [Fact]
    public async Task RepairRejectsAChangedFileWithoutResettingIt()
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "library.json");
        await File.WriteAllTextAsync(path, "[");
        var store = new LibraryStore(path);
        var loaded = await store.LoadAsync();
        await File.WriteAllTextAsync(path, "[{\"id\":\"new\"}]");
        await Assert.ThrowsAsync<LibraryConflictException>(() => store.RepairAsync(loaded.Revision));
        Assert.Equal("[{\"id\":\"new\"}]", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(dir, "*.bak"));
    }
}
