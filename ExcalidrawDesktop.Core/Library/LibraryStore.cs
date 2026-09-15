using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExcalidrawDesktop.Core;

public sealed record LibraryLoadResult(string Status, string Content, string Revision);
public sealed record LibrarySaveResult(string Status, string Content, string Revision);
public sealed class LibraryCorruptException(JsonException innerException)
    : IOException("The saved library is damaged and must be repaired before saving.", innerException);
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
            var bytes = await File.ReadAllBytesAsync(path);
            var content = Decode(bytes);
            try { Validate(content); }
            catch (JsonException) { return new("corrupt", "[]", Revision(bytes)); }
            return Result(content);
        }
        catch (JsonException) { return new("unavailable", "[]", ""); }
        catch (ArgumentOutOfRangeException) { return new("unavailable", "[]", ""); }
        catch (IOException) { return new("unavailable", "[]", ""); }
        catch (UnauthorizedAccessException) { return new("unavailable", "[]", ""); }
        finally { gate.Release(); }
    }
    /// <summary>Explicitly resets the exact damaged version the user reviewed, preserving its bytes.</summary>
    public async Task<LibraryLoadResult> RepairAsync(string expectedRevision)
    {
        await gate.WaitAsync();
        try
        {
            if (new FileInfo(path).Length > MaxBytes) throw new IOException("The library is too large to repair.");
            var bytes = await File.ReadAllBytesAsync(path);
            if (!string.Equals(Revision(bytes), expectedRevision, StringComparison.Ordinal))
                throw new LibraryConflictException("[]", "");
            try
            {
                Validate(Decode(bytes));
            }
            catch (JsonException)
            {
                await AtomicFile.WriteAllBytesAsync(path + ".corrupt-" + Guid.NewGuid().ToString("N") + ".bak", bytes);
                await AtomicFile.WriteAllTextAsync(path, "[]");
                return Result("[]");
            }
            throw new LibraryConflictException("[]", "");
        }
        finally { gate.Release(); }
    }
    public async Task<LibrarySaveResult> SaveAsync(string content, string expectedRevision)
    {
        Validate(content);
        await gate.WaitAsync();
        try
        {
            var current = File.Exists(path) ? await File.ReadAllTextAsync(path) : "[]";
            try { Validate(current); }
            catch (JsonException exception) { throw new LibraryCorruptException(exception); }
            var revision = Revision(current);
            if (!string.Equals(revision, expectedRevision ?? "", StringComparison.Ordinal))
                throw new LibraryConflictException(current, revision);
            await AtomicFile.WriteAllTextAsync(path, content);
            return new("saved", content, Revision(content));
        }
        finally { gate.Release(); }
    }
    private LibraryLoadResult Result(string content) => new("loaded", content, Revision(content));
    private static string Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    private static string Revision(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Validate(string content)
    {
        if (content is null || Encoding.UTF8.GetByteCount(content) > MaxBytes) throw new ArgumentOutOfRangeException(nameof(content));
        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Library must be an array.");
    }
}
