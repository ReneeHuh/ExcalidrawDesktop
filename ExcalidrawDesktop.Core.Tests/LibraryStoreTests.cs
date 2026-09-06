using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class LibraryStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "ExcalidrawLibrary-" + Guid.NewGuid().ToString("N"));
    [Fact] public async Task PersistsAndReloadsWithRevision()
    { var store = new LibraryStore(Path.Combine(dir, "library.json")); var initial = await store.LoadAsync(); var saved = await store.SaveAsync("[{\"id\":1}]", initial.Revision); var loaded = await store.LoadAsync(); Assert.Equal(saved.Revision, loaded.Revision); Assert.Equal(saved.Content, loaded.Content); }
    [Fact] public async Task RejectsStaleRevisionWithoutOverwrite()
    { var store = new LibraryStore(Path.Combine(dir, "library.json")); var first = await store.LoadAsync(); await store.SaveAsync("[1]", first.Revision); await Assert.ThrowsAsync<LibraryConflictException>(() => store.SaveAsync("[2]", first.Revision)); Assert.Equal("[1]", (await store.LoadAsync()).Content); }
    public void Dispose() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
