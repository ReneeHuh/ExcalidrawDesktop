using System.Text.Json;

namespace ExcalidrawDesktop.Core;

public sealed record CloseBarrierAcknowledgement(Guid BarrierId, bool IsDirty, bool HasUnsavedLibrary)
{
    public static CloseBarrierAcknowledgement? Read(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("barrierId", out var id) || id.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(id.GetString(), "D", out var barrierId) ||
            !value.TryGetProperty("isDirty", out var dirty) ||
            dirty.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !value.TryGetProperty("canClose", out var canClose) ||
            canClose.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
        return new(barrierId, dirty.GetBoolean(), !canClose.GetBoolean());
    }
}
