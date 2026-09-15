using System.Text.Json;

namespace ExcalidrawDesktop.Core;

public sealed record BridgeMessage(
    int Version,
    string Kind,
    string RequestId,
    string Method,
    JsonElement? Payload);

public sealed class BridgeProtocolException : Exception
{
    public BridgeProtocolException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public static class BridgeMessageParser
{
    public const int MaxIncomingMessageCharacters = 128 * 1024 * 1024;

    public static BridgeMessage Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxIncomingMessageCharacters)
        {
            throw new BridgeProtocolException(
                "BridgeMessageSizeInvalid",
                "The bridge message is empty or exceeds the incoming-message limit.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("The bridge message must be a JSON object.");
            }

            var version = ReadRequiredInt32(root, "version");
            if (version != 1)
            {
                throw new BridgeProtocolException(
                    "BridgeVersionUnsupported",
                    "The bridge message version is not supported.");
            }

            var kind = ReadRequiredString(root, "kind", 16);
            if (kind is not ("request" or "response" or "event"))
            {
                throw Invalid("The bridge message kind is invalid.");
            }

            var requestId = ReadRequiredString(root, "requestId", 128);
            if (!Guid.TryParseExact(requestId, "D", out _))
            {
                throw Invalid("The bridge request identifier must be a canonical UUID.");
            }

            var method = ReadRequiredString(root, "method", 128);
            var payload = root.TryGetProperty("payload", out var payloadElement)
                ? payloadElement.Clone()
                : (JsonElement?)null;

            return new BridgeMessage(version, kind, requestId, method, payload);
        }
        catch (JsonException exception)
        {
            throw new BridgeProtocolException(
                "BridgeMessageInvalid",
                $"The bridge message is not valid JSON: {exception.Message}");
        }
    }

    private static string ReadRequiredString(JsonElement root, string name, int maxLength)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"The bridge message requires a string '{name}'.");
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw Invalid($"The bridge message '{name}' is empty or too long.");
        }

        return value;
    }

    private static int ReadRequiredInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value))
        {
            throw Invalid($"The bridge message requires an integer '{name}'.");
        }

        return value;
    }

    private static BridgeProtocolException Invalid(string message) =>
        new("BridgeMessageInvalid", message);
}

internal static class BridgeEnvelopeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class BridgeResponseJson
{
    private static readonly JsonSerializerOptions Options = BridgeEnvelopeJson.Options;

    public static string Success(BridgeMessage request, object? payload) =>
        JsonSerializer.Serialize(new
        {
            version = 1,
            kind = "response",
            requestId = request.RequestId,
            method = request.Method,
            payload,
        }, Options);

    public static string Error(BridgeMessage request, string code, string message) =>
        JsonSerializer.Serialize(new
        {
            version = 1,
            kind = "response",
            requestId = request.RequestId,
            method = request.Method,
            error = new { code, message },
        }, Options);
}

public static class BridgeEventJson
{
    private static readonly JsonSerializerOptions Options = BridgeEnvelopeJson.Options;

    public static string Create(string method, object? payload = null) =>
        JsonSerializer.Serialize(new
        {
            version = 1,
            kind = "event",
            requestId = Guid.NewGuid().ToString("D"),
            method,
            payload,
        }, Options);
}
