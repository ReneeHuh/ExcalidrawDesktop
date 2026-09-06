#if DEBUG
using System.Text.Json;
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private async Task VerifyDocumentDirtySmokeAsync(DocumentSession session)
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            $"document-dirty-smoke-{Guid.NewGuid():N}.excalidraw");
        const string original = """{"type":"excalidraw","elements":[],"appState":{"viewBackgroundColor":"#ffffff"},"files":{}}""";
        try
        {
            await File.WriteAllTextAsync(path, original);
            documents.AttachDocumentToSession(session,
                await session.DocumentService.OpenPathAsync(path), select: true);
            await WaitForRetrySmokeReadyAsync(session, isDirty: false);

            await UpdateDirtySmokeAppStateAsync(session, new { viewBackgroundColor = "#ff0000" });
            if (!await AsyncWait.UntilAsync(() => session.IsDirty, TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("A background-only edit was not marked dirty.");
            }

            // Keep moving the viewport beyond the recovery debounce interval.
            // This must not postpone the pending document snapshot indefinitely.
            for (var index = 0; index < 10; index++)
            {
                await UpdateDirtySmokeAppStateAsync(session, new { scrollX = index * 25 });
                await Task.Delay(400);
            }
            var snapshot = await recoverySnapshotStore.LoadAsync(session.RecoveryId);
            if (snapshot is null || ReadDirtySmokeBackground(snapshot) != "#ff0000")
            {
                throw new InvalidOperationException("Viewport activity prevented background recovery.");
            }
            var snapshotTime = session.RecoveryUpdatedAt;
            await UpdateDirtySmokeAppStateAsync(session, new { scrollY = 125, zoom = new { value = 1.25 } });
            await Task.Delay(2300);
            if (session.RecoveryUpdatedAt != snapshotTime)
            {
                throw new InvalidOperationException("Viewport activity duplicated the recovery snapshot.");
            }

            // A second settings edit has the same element version but needs a
            // new snapshot, which Retry must restore without touching the file.
            await UpdateDirtySmokeAppStateAsync(session, new { viewBackgroundColor = "#00ff00" });
            if (!await AsyncWait.UntilAsync(() => session.RecoveryUpdatedAt != snapshotTime,
                    TimeSpan.FromSeconds(8)))
            {
                throw new InvalidOperationException("A second background edit did not update recovery.");
            }
            SimulateWebViewProcessFailureForSmoke(session);
            await editorSessions.RetryAsync(session);
            await WaitForRetrySmokeReadyAsync(session, isDirty: true);
            if ((await ReadSmokeStateAsync(session)).ViewBackgroundColor != "#00ff00" ||
                await File.ReadAllTextAsync(path) != original)
            {
                throw new InvalidOperationException("Retry did not preserve the background-only edit.");
            }

            RequestSessionSave(session);
            if (!await AsyncWait.UntilAsync(() => !session.IsDirty && !session.IsBridgeDispatching,
                    TimeSpan.FromSeconds(10)) ||
                ReadDirtySmokeBackground(await File.ReadAllTextAsync(path)) != "#00ff00")
            {
                throw new InvalidOperationException("Saving the background did not establish a clean baseline.");
            }
            await UpdateDirtySmokeAppStateAsync(session, new { scrollX = -100, selectedElementIds = new { } });
            await Task.Delay(300);
            if (session.IsDirty)
            {
                throw new InvalidOperationException("Viewport activity marked a saved drawing dirty.");
            }

            await UpdateDirtySmokeAppStateAsync(session, new { viewBackgroundColor = "#0000ff" });
            if (!await AsyncWait.UntilAsync(() => session.IsDirty, TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("A new background edit was not detected after saving.");
            }
            await UpdateDirtySmokeAppStateAsync(session, new { viewBackgroundColor = "#00ff00" });
            if (!await AsyncWait.UntilAsync(() => !session.IsDirty, TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("Reverting the background did not clear dirty state.");
            }

            session.TryPostEditorMessage(BridgeEventJson.Create("app.automationEditRequested",
                new { elementId = "dirty-deletion-smoke" }));
            if (!await AsyncWait.UntilAsync(() => session.IsDirty, TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("Adding the deletion fixture did not mark the drawing dirty.");
            }
            RequestSessionSave(session);
            if (!await AsyncWait.UntilAsync(() => !session.IsDirty && !session.IsBridgeDispatching,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException("The shape could not be saved before deletion.");
            }
            await session.CoreWebView!.ExecuteScriptAsync(
                "window.__EXCALIDRAW_DESKTOP_SMOKE__.deleteAllElements()");
            if (!await AsyncWait.UntilAsync(() => session.IsDirty, TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException("Deleting the last shape did not mark the drawing dirty.");
            }
            RequestSessionSave(session);
            if (!await AsyncWait.UntilAsync(() => !session.IsDirty && !session.IsBridgeDispatching,
                    TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException("Saving a deleted shape did not clear dirty state.");
            }
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            if (saved.RootElement.GetProperty("elements").GetArrayLength() != 0)
            {
                throw new InvalidOperationException("The saved file still contains a deleted shape.");
            }
            await UpdateDirtySmokeAppStateAsync(session, new { scrollX = 321 });
            await Task.Delay(300);
            if (session.IsDirty)
            {
                throw new InvalidOperationException("Viewport activity marked a saved deletion dirty.");
            }
        }
        finally
        {
            session.DetachExternalFileWatcher();
            File.Delete(path);
        }
    }

    private static async Task<string> UpdateDirtySmokeAppStateAsync(DocumentSession session, object state) =>
        await session.CoreWebView!.ExecuteScriptAsync(
            $"window.__EXCALIDRAW_DESKTOP_SMOKE__.updateAppState({JsonSerializer.Serialize(state)})");

    private static string? ReadDirtySmokeBackground(string content)
    {
        using var document = JsonDocument.Parse(content);
        return document.RootElement.GetProperty("appState").GetProperty("viewBackgroundColor").GetString();
    }
}
#endif
