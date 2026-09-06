using System.Diagnostics;
using System.Text.Json;
using ExcalidrawDesktop.Core;
using Microsoft.Web.WebView2.Core;

namespace ExcalidrawDesktop.App.Services;

public sealed class BridgeDispatcher
{
    private readonly DocumentService documentService;
    private CancellationTokenSource? pendingSaveCancellation;
    private string? pendingSaveRequestId;
    private Guid? pendingCloseRequestId;
    public Func<Guid, bool>? IsCloseSavePending { get; init; }

    public void CancelPendingSave() => pendingSaveCancellation?.Cancel();

    public void CancelCloseSave(Guid closeRequestId)
    {
        if (pendingCloseRequestId == closeRequestId)
        {
            CancelPendingSave();
        }
    }
    private readonly Action appReady;
    private readonly Action<Guid> closeReady;
    private readonly Action<bool> dirtyChanged;
    private readonly Action documentCreated;
    private readonly Action<string> documentOpened;
    // Save completion is separate from load completion: a save must not
    // clear a recovery snapshot for edits made after the save started.
    private readonly Action<string>? documentSaved;
    private readonly Action documentRecovered;
    private readonly Action? documentLoadFailed;
    private readonly Func<string, Task<bool>> recoverySnapshotReceived;
    private readonly Action externalConflictDetected;
    private readonly Action<Guid> closeCancelled;
    private readonly Action newTabRequested;
    private readonly Func<Task> openTabRequested;
    private readonly Action closeTabRequested;
    private readonly Action<bool> selectAdjacentTabRequested;
    private readonly Action<Guid, string> imageExportFailed;
    private readonly Action<string, string>? languageApplied;

    public BridgeDispatcher(
        DocumentService documentService,
        Action appReady,
        Action<Guid> closeReady,
        Action<bool> dirtyChanged,
        Action documentCreated,
        Action<string> documentOpened,
        Action documentRecovered,
        Func<string, Task<bool>> recoverySnapshotReceived,
        Action externalConflictDetected,
        Action<Guid> closeCancelled,
        Action newTabRequested,
        Func<Task> openTabRequested,
        Action closeTabRequested,
        Action<bool> selectAdjacentTabRequested,
        Action<Guid, string> imageExportFailed,
        Action<string, string>? languageApplied = null,
        Action? documentLoadFailed = null,
        Action<string>? documentSaved = null)
    {
        this.documentService = documentService;
        this.appReady = appReady;
        this.closeReady = closeReady;
        this.dirtyChanged = dirtyChanged;
        this.documentCreated = documentCreated;
        this.documentOpened = documentOpened;
        this.documentSaved = documentSaved;
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
                "document.recoverySnapshot" => await SaveRecoverySnapshotAsync(message.Payload),
                "document.new" => await NewDocumentAsync(message.Payload),
                "document.save" => await SaveDocumentAsync(webView, message, saveAs: false),
                "document.saveAs" => await SaveDocumentAsync(webView, message, saveAs: true),
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
        if (message.Method == "document.cancelSave" &&
            message.Payload is { ValueKind: JsonValueKind.Object } cancelPayload &&
            cancelPayload.TryGetProperty("requestId", out var cancelledSaveId) &&
            cancelledSaveId.ValueKind == JsonValueKind.String &&
            string.Equals(cancelledSaveId.GetString(), pendingSaveRequestId, StringComparison.Ordinal))
        {
            CancelPendingSave();
            return;
        }

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

        if (message.Method == "app.closeReady" && ReadCloseRequestId(message.Payload) is { } readyId)
        {
            closeReady(readyId);
            return;
        }

        if (message.Method == "app.closeCancelled" && ReadCloseRequestId(message.Payload) is { } cancelledId)
        {
            closeCancelled(cancelledId);
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

    }

    private async Task<object> SaveRecoverySnapshotAsync(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("content", out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.String || contentElement.GetString() is not { } content)
        {
            throw new BridgeProtocolException("BridgePayloadInvalid", "The recovery snapshot payload is invalid.");
        }
        ExcalidrawDocumentValidator.ValidateForSave(content, DocumentService.MaxDocumentBytes);
        return new { status = await recoverySnapshotReceived(content) ? "stored" : "ignored" };
    }

    private async Task<DocumentNewResult> NewDocumentAsync(JsonElement? payload)
    {
        var hasUnsavedChanges = ReadDirtyFlag(payload, "document.new");
        return await documentService.NewAsync(hasUnsavedChanges);
    }

    private async Task<DocumentSaveResult> SaveDocumentAsync(
        CoreWebView2 webView,
        BridgeMessage message,
        bool saveAs)
    {
        var payload = message.Payload;
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("content", out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.String ||
            contentElement.GetString() is not { } content)
        {
            throw new BridgeProtocolException(
                "BridgePayloadInvalid",
                $"The document.{(saveAs ? "saveAs" : "save")} payload is invalid.");
        }

        var closeRequestId = ReadCloseRequestId(payload);
        if (value.TryGetProperty("closeRequestId", out _) && closeRequestId is null)
        {
            throw new BridgeProtocolException("BridgePayloadInvalid", "The close save identifier is invalid.");
        }
        if (closeRequestId is { } closeId && IsCloseSavePending?.Invoke(closeId) != true)
        {
            return DocumentSaveResult.Cancelled;
        }
        if (pendingSaveCancellation is not null || documentService.IsSaving)
        {
            throw new BridgeProtocolException("DocumentSaveInProgress", "Wait for the current save to finish.");
        }
        using var cancellation = new CancellationTokenSource();
        pendingSaveCancellation = cancellation;
        pendingSaveRequestId = message.RequestId;
        pendingCloseRequestId = closeRequestId;
        void PickerChanged(bool isPickerOpen) => TryPostResponse(webView,
            BridgeEventJson.SaveProgress(message.RequestId, isPickerOpen));
        documentService.SavePickerChanged += PickerChanged;
        try
        {
            var result = await documentService.SaveAsync(content, saveAs, cancellation.Token);
            if (result.Status == "saved" && result.FileName is { } fileName)
            {
                (documentSaved ?? documentOpened)(fileName);
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return DocumentSaveResult.Cancelled;
        }
        catch (BridgeProtocolException exception) when (exception.Code is
            "DocumentChangedExternally" or "DocumentUnavailable" or "DocumentBaselineUnknown")
        {
            externalConflictDetected();
            throw;
        }
        finally
        {
            documentService.SavePickerChanged -= PickerChanged;
            pendingSaveCancellation = null;
            pendingSaveRequestId = null;
            pendingCloseRequestId = null;
        }
    }

    private static Guid? ReadCloseRequestId(JsonElement? payload) =>
        payload is { ValueKind: JsonValueKind.Object } value &&
        value.TryGetProperty("closeRequestId", out var id) &&
        id.ValueKind == JsonValueKind.String &&
        Guid.TryParseExact(id.GetString(), "D", out var requestId)
            ? requestId : null;

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
