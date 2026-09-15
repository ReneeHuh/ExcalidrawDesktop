using System.Text.Json;
using System.Text;

namespace ExcalidrawDesktop.Core;

public static class ExcalidrawDocumentValidator
{
    public static void ValidateForSave(string content, ulong maxBytes)
    {
        if ((ulong)Encoding.UTF8.GetByteCount(content) > maxBytes)
        {
            throw new BridgeProtocolException(
                "DocumentTooLarge",
                "The drawing exceeds the 50 MB desktop document limit.");
        }

        Validate(content);
    }

    public static void Validate(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String ||
                type.GetString() != "excalidraw" ||
                !root.TryGetProperty("elements", out var elements) ||
                elements.ValueKind != JsonValueKind.Array)
            {
                throw Invalid();
            }

            if (root.TryGetProperty("appState", out var appState) &&
                appState.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
            {
                throw Invalid();
            }

            if (root.TryGetProperty("files", out var files) &&
                files.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
            {
                throw Invalid();
            }
        }
        catch (JsonException)
        {
            throw Invalid();
        }
    }

    private static BridgeProtocolException Invalid() =>
        new("DocumentInvalid", "The drawing is not a valid Excalidraw document.");
}
