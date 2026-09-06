using System.Text.Json;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private async Task<object?> HandleLibraryRequestAsync(BridgeMessage message)
    {
        if (message.Method == "library.load")
            return await workspaceCoordinator.LoadLibraryAsync();
        if (message.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("expectedRevision", out var revision) || revision.ValueKind != JsonValueKind.String)
            throw new BridgeProtocolException("LibraryInvalid", "The library save requires its previous revision and content.");
        try
        {
            return await workspaceCoordinator.SaveLibraryAsync(content.GetString()!, revision.GetString()!);
        }
        catch (LibraryConflictException)
        {
            throw new BridgeProtocolException("LibraryConflict", "The shared library changed in another tab. Reload it before saving.");
        }
    }

    private void OnSharedLibraryChanged(LibraryLoadResult library)
    {
        foreach (var session in sessions)
            session.TryPostEditorMessage(BridgeEventJson.Create("library.changed",
                new { content = library.Content, revision = library.Revision }));
    }
}
