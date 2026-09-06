using System.Text.Json;
using System.Buffers;
using System.Text.Encodings.Web;
using System.Security.Cryptography;
using System.Text;

namespace ExcalidrawDesktop.Core;

/// <summary>The disk version from which a recovered drawing was edited.</summary>
public sealed record RecoveryFileBaseline(string Path, DesktopFileStamp Stamp, string? ContentHash = null)
{
    private const string PropertyName = "desktopRecovery";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>Returns a stable SHA-256 fingerprint of the exact UTF-8 bytes saved to disk.</summary>
    public static string ComputeContentHash(string content) =>
        ComputeContentHash(Encoding.UTF8.GetBytes(content));

    public static string ComputeContentHash(ReadOnlySpan<byte> utf8Bytes) =>
        Convert.ToHexString(SHA256.HashData(utf8Bytes));

    public static async Task<string> ComputeContentHashAsync(Stream stream, CancellationToken cancellationToken = default) =>
        Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));

    public bool MatchesContent(string content) =>
        string.IsNullOrWhiteSpace(ContentHash) ||
        string.Equals(ContentHash, ComputeContentHash(content), StringComparison.OrdinalIgnoreCase);

    public static string Attach(string content, RecoveryFileBaseline? baseline)
        => Encoding.UTF8.GetString(AttachUtf8(content, baseline).Span);

    public static ReadOnlyMemory<byte> AttachUtf8(string content, RecoveryFileBaseline? baseline)
        => RewriteMetadata(content, baseline, includeMetadata: true);

    /// <summary>Removes native recovery metadata before validating document size.</summary>
    public static string GetDocumentContent(string content)
        => Encoding.UTF8.GetString(RewriteMetadata(content, null, includeMetadata: false).Span);

    private static ReadOnlyMemory<byte> RewriteMetadata(string content, RecoveryFileBaseline? baseline, bool includeMetadata)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("The recovery drawing must be an object.");
        var buffer = new ArrayBufferWriter<byte>(checked(bytes.Length + 1024));
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = Options.Encoder });
        writer.WriteStartObject();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            var nativeMetadata = reader.ValueTextEquals(PropertyName);
            if (!nativeMetadata) writer.WritePropertyName(reader.GetString()!);
            if (!reader.Read()) throw new JsonException();
            var start = checked((int)reader.TokenStartIndex);
            // Skip validates the value without building a scene-sized mutable DOM.
            reader.Skip();
            if (!nativeMetadata)
                writer.WriteRawValue(bytes.AsSpan(start, checked((int)reader.BytesConsumed) - start), skipInputValidation: true);
        }
        if (reader.TokenType != JsonTokenType.EndObject || reader.Read()) throw new JsonException();
        // Remove every incoming copy, including escaped property names, before
        // writing the baseline that the native document service actually owns.
        if (includeMetadata)
        {
            writer.WritePropertyName(PropertyName);
            JsonSerializer.Serialize(writer, baseline, Options);
        }
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    public static DesktopFileStamp? ReadStamp(string content, string? expectedPath)
        => Read(content, expectedPath)?.Stamp;

    public static RecoveryFileBaseline? ReadEmbedded(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(PropertyName, out var metadata) ||
                metadata.ValueKind != JsonValueKind.Object ||
                !metadata.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String ||
                DesktopDocumentPath.Normalize(path.GetString()) is not { } canonicalPath) return null;
            return ReadMetadata(metadata, canonicalPath);
        }
        catch (JsonException) { return null; }
    }

    public static RecoveryFileBaseline? Read(string content, string? expectedPath)
    {
        if (string.IsNullOrWhiteSpace(expectedPath))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(PropertyName, out var metadata) ||
                metadata.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            return ReadMetadata(metadata, expectedPath);
        }
        catch (JsonException)
        {
            // Old or damaged metadata cannot establish that overwriting is safe.
            return null;
        }
    }

    private static RecoveryFileBaseline? ReadMetadata(JsonElement metadata, string expectedPath)
    {
        var baseline = metadata.Deserialize<RecoveryFileBaseline>(Options);
        return baseline is { Path: not null, Stamp: not null } &&
            DesktopDocumentPath.Equals(baseline.Path, expectedPath) ? baseline : null;
    }
}
