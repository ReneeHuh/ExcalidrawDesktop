using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExcalidrawDesktop.Core;

public sealed record LibraryLoadResult(string Status, string Content, string Revision);
public sealed record LibrarySaveResult(string Status, string Content, string Revision);
public sealed class LibraryConflictException(string content, string revision) : IOException("The shared library changed before it could be saved.")
{
    public string Content { get; } = content;
    public string Revision { get; } = revision;
}

public sealed class LibraryStore
{
    public const int MaxBytes = 20 * 1024 * 1024;
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    public LibraryStore(string path) => this.path = Path.GetFullPath(path);
    public async Task<LibraryLoadResult> LoadAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!File.Exists(path)) return Result("[]");
            if (new FileInfo(path).Length > MaxBytes) return new("unavailable", "[]", "");
            var content = await File.ReadAllTextAsync(path);
            Validate(content);
            return Result(content);
        }
        catch (JsonException) { return new("unavailable", "[]", ""); }
        catch (ArgumentOutOfRangeException) { return new("unavailable", "[]", ""); }
        catch (IOException) { return new("unavailable", "[]", ""); }
        catch (UnauthorizedAccessException) { return new("unavailable", "[]", ""); }
        finally { gate.Release(); }
    }
    public async Task<LibrarySaveResult> SaveAsync(string content, string expectedRevision)
    {
        Validate(content);
        await gate.WaitAsync();
        try
        {
            var current = File.Exists(path) ? await File.ReadAllTextAsync(path) : "[]";
            Validate(current);
            var revision = Revision(current);
            if (!string.Equals(revision, expectedRevision ?? "", StringComparison.Ordinal))
                throw new LibraryConflictException(current, revision);
            await AtomicFile.WriteAllTextAsync(path, content);
            return new("saved", content, Revision(content));
        }
        finally { gate.Release(); }
    }
    private LibraryLoadResult Result(string content) => new("loaded", content, Revision(content));
    private static string Revision(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    private static void Validate(string content)
    {
        if (content is null || Encoding.UTF8.GetByteCount(content) > MaxBytes) throw new ArgumentOutOfRangeException(nameof(content));
        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Library must be an array.");
    }
}
