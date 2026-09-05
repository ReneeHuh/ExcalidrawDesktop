using System.Diagnostics;
using System.Text.Json;
using ExcalidrawDesktop.Core;
using Microsoft.Web.WebView2.Core;

namespace ExcalidrawDesktop.App.Services;

public sealed class BridgeDispatcher
{
    private readonly DocumentService documentService;
    private readonly Action appReady;
    private readonly Action closeReady;
    private readonly Action<bool> dirtyChanged;
    private readonly Action documentCreated;
    private readonly Action<string> documentOpened;
    private readonly Action documentRecovered;
    private readonly Action? documentLoadFailed;
    private readonly Func<string, Task> recoverySnapshotReceived;
    private readonly Action externalConflictDetected;
    private readonly Action closeCancelled;
    private readonly Action newTabRequested;
    private readonly Func<Task> openTabRequested;
    private readonly Action closeTabRequested;
    private readonly Action<bool> selectAdjacentTabRequested;
    private readonly Action<Guid, string> imageExportFailed;
    private readonly Action<string, string>? languageApplied;

    public BridgeDispatcher(
        DocumentService documentService,
        Action appReady,
        Action closeReady,
        Action<bool> dirtyChanged,
        Action documentCreated,
        Action<string> documentOpened,
        Action documentRecovered,
        Func<string, Task> recoverySnapshotReceived,
        Action externalConflictDetected,
        Action closeCancelled,
        Action newTabRequested,
        Func<Task> openTabRequested,
        Action closeTabRequested,
        Action<bool> selectAdjacentTabRequested,
        Action<Guid, string> imageExportFailed,
        Action<string, string>? languageApplied = null,
        Action? documentLoadFailed = null)
    {
        this.documentService = documentService;
        this.appReady = appReady;
        this.closeReady = closeReady;
        this.dirtyChanged = dirtyChanged;
        this.documentCreated = documentCreated;
        this.documentOpened = documentOpened;
        this.documentRecovered = documentRecovered;
        this.recoverySnapshotReceived = recoverySnapshotReceived;
        this.externalConflictDetected = externalConflictDetected;
        this.closeCancelled = closeCancelled;
        this.newTabRequested = newTabRequested;
        this.openTabRequested = openTabRequested;
        this.closeTabRequested = closeTabRequested;
        this.selectAdjacentTabRequested = selectAdjacentTabRequested;
        this.imageExportFailed = imageExportFailed;
        this.languageApplied = languageApplied;
        this.documentLoadFailed = documentLoadFailed;
    }

    public async Task DispatchAsync(CoreWebView2 webView, BridgeMessage message)
    {
        if (message.Kind == "event")
        {
            await DispatchEventAsync(message);
            return;
        }

        if (message.Kind != "request")
        {
            return;
        }

        try
        {
            object? response = message.Method switch
            {
                "app.ping" => new { host = "winui", ready = true },
                "document.new" => await NewDocumentAsync(message.Payload),
                "document.open" => await OpenDocumentAsync(message.Payload),
                "document.save" => await SaveDocumentAsync(message.Payload, saveAs: false),
                "document.saveAs" => await SaveDocumentAsync(message.Payload, saveAs: true),
                _ => throw new BridgeProtocolException(
                    "BridgeMethodNotFound",
                    "The requested desktop bridge method is not available."),
            };
            TryPostResponse(webView, BridgeResponseJson.Success(message, response));
        }
        catch (BridgeProtocolException exception)
        {
            TryPostResponse(
                webView,
                BridgeResponseJson.Error(message, exception.Code, exception.Message));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            TryPostResponse(
                webView,
                BridgeResponseJson.Error(
                    message,
                    "InternalError",
                    "Excalidraw Desktop could not complete the request."));
        }
    }

    /// <summary>
    /// Posts a bridge response, tolerating a CoreWebView2 that was closed or
    /// crashed while the request was in flight. The editor session surfaces
    /// that failure through its own lifecycle handling; the dispatcher must
    /// not let it escape into the async void message handler.
    /// </summary>
    private static bool TryPostResponse(CoreWebView2 webView, string json)
    {
        try
        {
            webView.PostWebMessageAsJson(json);
            return true;
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.COMException or
            ObjectDisposedException or
            InvalidOperationException)
        {
            Debug.WriteLine($"Bridge response dropped; the editor is no longer reachable: {exception.Message}");
            DiagnosticLogService.Info("bridge.response_dropped", new
            {
                reason = exception.GetType().Name,
                hresult = exception.HResult,
            });
            return false;
        }
    }

    private async Task DispatchEventAsync(BridgeMessage message)
    {
        if (message.Method == "document.loadFailed")
        {
            documentLoadFailed?.Invoke();
            return;
        }

        if (message.Method == "app.ready")
        {
            appReady();
            return;
        }

        if (message.Method == "app.languageApplied" &&
            message.Payload is { ValueKind: JsonValueKind.Object } languagePayload &&
            languagePayload.TryGetProperty("langCode", out var langCodeElement) &&
            langCodeElement.ValueKind == JsonValueKind.String &&
            langCodeElement.GetString() is { Length: > 0 and <= 16 } langCode &&
            languagePayload.TryGetProperty(
                "direction",
                out var languageDirectionElement) &&
            languageDirectionElement.ValueKind == JsonValueKind.String &&
            languageDirectionElement.GetString() is { } languageDirection &&
            languageDirection is "ltr" or "rtl")
        {
            languageApplied?.Invoke(langCode, languageDirection);
            return;
        }

        if (message.Method == "app.closeReady")
        {
            closeReady();
            return;
        }

        if (message.Method == "app.closeCancelled")
        {
            closeCancelled();
            return;
        }

        if (message.Method == "workspace.newTabRequested")
        {
            newTabRequested();
            return;
        }

        if (message.Method == "workspace.openRequested")
        {
            await openTabRequested();
            return;
        }

        if (message.Method == "workspace.closeTabRequested")
        {
            closeTabRequested();
            return;
        }

        if (message.Method == "workspace.selectAdjacentTabRequested" &&
            message.Payload is { ValueKind: JsonValueKind.Object } adjacentPayload &&
            adjacentPayload.TryGetProperty("direction", out var directionElement) &&
            directionElement.ValueKind == JsonValueKind.String &&
            directionElement.GetString() is { } direction &&
            direction is "next" or "previous")
        {
            selectAdjacentTabRequested(direction == "next");
            return;
        }

        if (message.Method == "document.created" && documentService.ConfirmCreated())
        {
            documentCreated();
            return;
        }

        if (message.Method == "document.dirtyChanged" &&
            message.Payload is { ValueKind: JsonValueKind.Object } dirtyPayload &&
            dirtyPayload.TryGetProperty("isDirty", out var dirtyElement) &&
            dirtyElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            dirtyChanged(dirtyElement.GetBoolean());
            return;
        }

        if (message.Method == "document.recovered")
        {
            documentRecovered();
            return;
        }

        if (message.Method == "document.recoverySnapshot" &&
            message.Payload is { ValueKind: JsonValueKind.Object } recoveryPayload &&
            recoveryPayload.TryGetProperty("content", out var recoveryContentElement) &&
            recoveryContentElement.ValueKind == JsonValueKind.String &&
            recoveryContentElement.GetString() is { } recoveryContent)
        {
            ExcalidrawDocumentValidator.ValidateForSave(
                recoveryContent,
                DocumentService.MaxDocumentBytes);
            await recoverySnapshotReceived(recoveryContent);
            return;
        }

        if (message.Method == "image.exportFailed" &&
            message.Payload is { ValueKind: JsonValueKind.Object } exportPayload &&
            exportPayload.TryGetProperty("exportId", out var exportIdElement) &&
            exportIdElement.ValueKind == JsonValueKind.String &&
            Guid.TryParseExact(exportIdElement.GetString(), "D", out var exportId) &&
            exportPayload.TryGetProperty("message", out var exportMessageElement) &&
            exportMessageElement.ValueKind == JsonValueKind.String &&
            exportMessageElement.GetString() is { Length: > 0 and <= 500 } exportMessage)
        {
            imageExportFailed(exportId, exportMessage);
            return;
        }

        if (message.Method == "document.opened" &&
            message.Payload is { ValueKind: JsonValueKind.Object } payload &&
            payload.TryGetProperty("fileName", out var fileNameElement) &&
            fileNameElement.ValueKind == JsonValueKind.String &&
            fileNameElement.GetString() is { Length: > 0 and <= 260 } fileName &&
            documentService.ConfirmOpened(fileName))
        {
            documentOpened(fileName);
        }
    }

    private async Task<DocumentNewResult> NewDocumentAsync(JsonElement? payload)
    {
        var hasUnsavedChanges = ReadDirtyFlag(payload, "document.new");
        return await documentService.NewAsync(hasUnsavedChanges);
    }

    private async Task<DocumentSaveResult> SaveDocumentAsync(
        JsonElement? payload,
        bool saveAs)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("content", out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.String ||
            contentElement.GetString() is not { } content)
        {
            throw new BridgeProtocolException(
                "BridgePayloadInvalid",
                $"The document.{(saveAs ? "saveAs" : "save")} payload is invalid.");
        }

        DocumentSaveResult result;
        try
        {
            result = await documentService.SaveAsync(content, saveAs);
        }
        catch (BridgeProtocolException exception) when (
            exception.Code == "DocumentChangedExternally")
        {
            externalConflictDetected();
            throw;
        }
        if (result.Status == "saved" && result.FileName is { } fileName)
        {
            documentOpened(fileName);
        }

        return result;
    }

    private async Task<DocumentOpenResult> OpenDocumentAsync(JsonElement? payload)
    {
        var hasUnsavedChanges = ReadDirtyFlag(payload, "document.open");
        return await documentService.OpenAsync(hasUnsavedChanges);
    }

    private static bool ReadDirtyFlag(JsonElement? payload, string method)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("hasUnsavedChanges", out var dirtyElement) ||
            dirtyElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new BridgeProtocolException(
                "BridgePayloadInvalid",
                $"The {method} payload is invalid.");
        }

        return dirtyElement.GetBoolean();
    }
}
