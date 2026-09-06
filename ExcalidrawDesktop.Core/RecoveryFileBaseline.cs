using System.Text.Json;
using System.Text.Json.Nodes;
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

    public bool MatchesContent(string content) =>
        string.IsNullOrWhiteSpace(ContentHash) ||
        string.Equals(ContentHash, ComputeContentHash(content), StringComparison.OrdinalIgnoreCase);

    public static string Attach(string content, RecoveryFileBaseline? baseline)
    {
        var drawing = JsonNode.Parse(content)?.AsObject() ??
            throw new JsonException("The recovery drawing must be an object.");
        // Always replace any metadata received from the editor. Only the native
        // document service knows which disk version this snapshot belongs to.
        drawing[PropertyName] = JsonSerializer.SerializeToNode(baseline, Options);
        return drawing.ToJsonString(Options);
    }

    /// <summary>Removes native recovery metadata before validating document size.</summary>
    public static string GetDocumentContent(string content)
    {
        var drawing = JsonNode.Parse(content)?.AsObject() ??
            throw new JsonException("The recovery drawing must be an object.");
        drawing.Remove(PropertyName);
        return drawing.ToJsonString(Options);
    }

    public static DesktopFileStamp? ReadStamp(string content, string? expectedPath)
        => Read(content, expectedPath)?.Stamp;

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
            var baseline = metadata.Deserialize<RecoveryFileBaseline>(Options);
            return baseline is { Path: not null, Stamp: not null } &&
                DesktopDocumentPath.Equals(baseline.Path, expectedPath)
                    ? baseline : null;
        }
        catch (JsonException)
        {
            // Old or damaged metadata cannot establish that overwriting is safe.
            return null;
        }
    }
}
