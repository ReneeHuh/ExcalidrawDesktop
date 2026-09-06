using System.Text.Json;
using ExcalidrawDesktop.Core;
using ExcalidrawDesktop.App.Services;
using ExcalidrawDesktop.App.Models;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private readonly LibraryRepairSession libraryRepair = new();
#if DEBUG
    private int libraryRepairPromptsForSmoke;
#endif

    private async Task<object?> HandleLibraryRequestAsync(DocumentSession requestingSession, BridgeMessage message)
    {
        if (message.Method == "library.load")
        {
            var loaded = await workspaceCoordinator.LoadLibraryAsync();
            if (loaded.Status != "corrupt") return loaded;
            if (CommandsBlocked) return new LibraryLoadResult("unavailable", "[]", "");
            var retry = message.Payload is { ValueKind: JsonValueKind.Object } loadPayload &&
                loadPayload.TryGetProperty("retryRepair", out var retryRepair) && retryRepair.ValueKind == JsonValueKind.True;
            void SetPromptOpen(bool open) => requestingSession.TryPostEditorMessage(
                BridgeEventJson.SaveProgress(message.RequestId, open));
            SetPromptOpen(true);
            try
            {
                return await WindowModalCoordinator.For(this).RunAsync(() => libraryRepair.LoadAsync(
                    workspaceCoordinator.LoadLibraryAsync, workspaceCoordinator.RepairLibraryAsync, async () =>
                {
                    if (CommandsBlocked || !sessions.Contains(requestingSession)) return false;
                    var dialog = new ContentDialog
                    {
                        XamlRoot = DocumentTabs.XamlRoot,
                        Title = DesktopResources.Get("LibraryRepairTitle", "The saved library is damaged"),
                        Content = DesktopResources.Get("LibraryRepairContent", "Reset the shared library to continue saving library changes? The damaged file will be kept as a .bak file beside library.json. Your drawings will not be changed."),
                        PrimaryButtonText = DesktopResources.Get("LibraryResetButton", "Back up and reset"),
                        CloseButtonText = DesktopResources.Get("CancelButton", "Cancel"),
                        DefaultButton = ContentDialogButton.Close,
                    };
#if DEBUG
                    libraryRepairPromptsForSmoke++;
#endif
                    return await dialog.ShowAsync() == ContentDialogResult.Primary;
                }, retry));
            }
            finally { SetPromptOpen(false); }
        }
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
        catch (LibraryCorruptException)
        {
            throw new BridgeProtocolException("LibraryCorrupt", "The saved library is damaged. Reload it to repair it before saving.");
        }
    }

    private void OnSharedLibraryChanged(LibraryLoadResult library)
    {
        foreach (var session in sessions)
            session.TryPostEditorMessage(BridgeEventJson.Create("library.changed",
                new { content = library.Content, revision = library.Revision }));
    }
}
