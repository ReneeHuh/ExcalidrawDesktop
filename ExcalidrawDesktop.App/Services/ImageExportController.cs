using System.Diagnostics;
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ExcalidrawDesktop.App.Services;

internal interface IImageExportHost
{
    Window OwnerWindow { get; }
    DocumentSession? ActiveSession { get; }
    bool IsDisposed { get; }
    XamlRoot? DialogRoot { get; }
    void UpdateFileMenuState(DocumentSession? session);
    void UpdateStatusBar(DocumentSession session);
}

/// <summary>Owns PNG destination selection, upload handling, cancellation, and export status.</summary>
internal sealed class ImageExportController
{
    private readonly IImageExportHost host;
    private readonly IReadOnlyList<DocumentSession> sessions;
    private readonly DocumentLifecycleController documents;
    private readonly ImageExportService imageExportService = new();
    private readonly HashSet<Guid> committingExports = [];
#if DEBUG
    public Func<DocumentSession, Task>? BeforeUploadWriteForSmoke { get; set; }
    public Action? UploadFinishedForSmoke { get; set; }
    public Action<string>? ExportCompletedForSmoke { get; set; }
    public Func<string, bool>? ExportFailedForSmoke { get; set; }
#endif

    public ImageExportController(IImageExportHost host,
        IReadOnlyList<DocumentSession> sessions, DocumentLifecycleController documents)
    {
        this.host = host;
        this.sessions = sessions;
        this.documents = documents;
    }

    public async void OnImageExportWebResourceRequested(
        DocumentSession session,
        CoreWebView2 coreWebView,
        CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            await HandleImageExportWebResourceAsync(session, coreWebView, args);
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("image_export.callback_failed", exception);
            try
            {
                if (session.PendingImageExport is { } pending)
                {
                    await FailImageExportAsync(session, pending.ExportId, GetImageExportFailureMessage(exception));
                }
            }
            catch (Exception cleanupException)
            {
                DiagnosticLogService.Error("image_export.cleanup_failed", cleanupException);
            }
        }
#if DEBUG
        finally
        {
            UploadFinishedForSmoke?.Invoke();
        }
#endif
    }

    private async Task HandleImageExportWebResourceAsync(
        DocumentSession session,
        CoreWebView2 coreWebView,
        CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (session.PendingImageExport is not { } pending ||
            !sessions.Contains(session) ||
            !ReferenceEquals(session.CoreWebView, coreWebView) ||
            !ImageExportPolicy.IsMatchingUpload(
                args.Request.Uri,
                session.TabOrigin,
                pending.ExportId))
        {
            return;
        }

        string? ReadHeader(string name)
        {
            try
            {
                return args.Request.Headers.GetHeader(name);
            }
            catch
            {
                return null;
            }
        }

        var requestExportId = ReadHeader("X-Excalidraw-Export-Id");
        var contentType = ReadHeader("Content-Type");
        var requestOrigin = ReadHeader("Origin");
        var requestedMethod = ReadHeader("Access-Control-Request-Method");
        var requestedHeaders = ReadHeader("Access-Control-Request-Headers");

        if (!string.Equals(
                requestOrigin,
                session.TabOrigin.Origin,
                StringComparison.OrdinalIgnoreCase))
        {
            TrySetImageExportResponse(session, coreWebView, args,
                403,
                "Forbidden",
                session.TabOrigin.Origin);
            return;
        }

        if (string.Equals(
                args.Request.Method,
                "OPTIONS",
                StringComparison.OrdinalIgnoreCase))
        {
            var validPreflight = string.Equals(
                    requestedMethod,
                    "POST",
                    StringComparison.OrdinalIgnoreCase) &&
                requestedHeaders?.Contains(
                    "x-excalidraw-export-id",
                    StringComparison.OrdinalIgnoreCase) == true;
            TrySetImageExportResponse(session, coreWebView, args,
                validPreflight ? 204 : 400,
                validPreflight ? "No Content" : "Bad Request",
                session.TabOrigin.Origin,
                includePreflightHeaders: validPreflight);
            return;
        }

        if (!string.Equals(
                args.Request.Method,
                "POST",
                StringComparison.OrdinalIgnoreCase) ||
            args.Request.Content is null ||
            !string.Equals(
                requestExportId,
                pending.ExportId.ToString("D"),
                StringComparison.OrdinalIgnoreCase) ||
            contentType?.StartsWith("image/png", StringComparison.OrdinalIgnoreCase) != true)
        {
            TrySetImageExportResponse(session, coreWebView, args,
                400,
                "Bad Request",
                session.TabOrigin.Origin);
            _ = FailImageExportAsync(
                session,
                pending.ExportId,
                DesktopResources.Get(
                    "InvalidPngExportData",
                    "The editor sent invalid PNG export data."));
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            var cancellationToken = pending.Cancellation.Token;
#if DEBUG
            if (BeforeUploadWriteForSmoke is { } beforeWrite)
            {
                await beforeWrite(session);
            }
#endif
            cancellationToken.ThrowIfCancellationRequested();
            await ImageExportService.WritePngAsync(
                pending.Destination,
                args.Request.Content,
                cancellationToken,
                () => committingExports.Add(pending.ExportId));
            if (session.PendingImageExport?.ExportId != pending.ExportId)
            {
                throw new OperationCanceledException();
            }

            CompleteImageExport(session, pending, pending.Destination.Name);
            TrySetImageExportResponse(session, coreWebView, args,
                204,
                "No Content",
                session.TabOrigin.Origin);
        }
        catch (OperationCanceledException)
        {
            TrySetImageExportResponse(session, coreWebView, args,
                409,
                "Cancelled",
                session.TabOrigin.Origin);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"PNG export failed: {exception}");
            committingExports.Remove(pending.ExportId);
            _ = FailImageExportAsync(
                session,
                pending.ExportId,
                GetImageExportFailureMessage(exception));
            TrySetImageExportResponse(session, coreWebView, args,
                500,
                "Export Failed",
                session.TabOrigin.Origin);
        }
        finally
        {
            committingExports.Remove(pending.ExportId);
            try
            {
                deferral.Complete();
            }
            catch (Exception exception) when (IsClosedWebViewException(exception))
            {
                DiagnosticLogService.Info("image_export.deferral_dropped", new { reason = exception.GetType().Name });
            }
        }
    }

    private static bool IsClosedWebViewException(Exception exception) => exception is
        System.Runtime.InteropServices.COMException or ObjectDisposedException or InvalidOperationException;

    private bool TrySetImageExportResponse(DocumentSession session, CoreWebView2 coreWebView,
        CoreWebView2WebResourceRequestedEventArgs args, int statusCode, string reasonPhrase,
        string allowedOrigin, bool includePreflightHeaders = false)
    {
        if (host.IsDisposed || !sessions.Contains(session) || !ReferenceEquals(session.CoreWebView, coreWebView))
        {
            return false;
        }
        try
        {
            args.Response = CreateImageExportResponse(coreWebView, statusCode, reasonPhrase,
                allowedOrigin, includePreflightHeaders);
            return true;
        }
        catch (Exception exception) when (IsClosedWebViewException(exception))
        {
            DiagnosticLogService.Info("image_export.response_dropped", new { reason = exception.GetType().Name });
            return false;
        }
    }

    private static CoreWebView2WebResourceResponse CreateImageExportResponse(
        CoreWebView2 coreWebView,
        int statusCode,
        string reasonPhrase,
        string allowedOrigin,
        bool includePreflightHeaders = false)
    {
        var headers = $"Access-Control-Allow-Origin: {allowedOrigin}\r\n" +
            "Vary: Origin\r\nContent-Type: text/plain";
        if (includePreflightHeaders)
        {
            headers += "\r\nAccess-Control-Allow-Methods: POST" +
                "\r\nAccess-Control-Allow-Headers: Content-Type, X-Excalidraw-Export-Id" +
                "\r\nAccess-Control-Max-Age: 600";
        }
        return coreWebView.Environment.CreateWebResourceResponse(
            new InMemoryRandomAccessStream(),
            statusCode,
            reasonPhrase,
            headers);
    }

    public async Task ExportActiveSessionAsPngAsync()
    {
        if (documents.IsPickerActive ||
            host.ActiveSession is not
            {
                IsReady: true,
                IsExporting: false,
                IsResuming: false,
                IsRestoringFromHibernation: false,
                CoreWebView: { } coreWebView,
            } session)
        {
            return;
        }

        documents.IsPickerActive = true;
        host.UpdateFileMenuState(session);
        try
        {
            var destination = await imageExportService.PickDestinationAsync(
                host.OwnerWindow,
                session.DisplayName);
            if (destination is null ||
                !sessions.Contains(session) ||
                !ReferenceEquals(session.CoreWebView, coreWebView))
            {
                return;
            }

            StartImageExport(session, coreWebView, destination);
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error(
                "image_export.start_failed",
                exception,
                new { sessionId = session.RecoveryId });
            Debug.WriteLine($"Could not start PNG export: {exception}");
            if (session.PendingImageExport is { } pending)
            {
                await FailImageExportAsync(
                    session,
                    pending.ExportId,
                    DesktopResources.Get(
                        "PngExportStartFailed",
                        "The PNG export could not be started."));
            }
            else
            {
                await ShowImageExportErrorAsync(
                    DesktopResources.Get(
                        "PngExportStartFailed",
                        "The PNG export could not be started."));
            }
        }
        finally
        {
            documents.IsPickerActive = false;
            host.UpdateFileMenuState(host.ActiveSession);
        }
    }

    public void StartImageExport(
        DocumentSession session,
        CoreWebView2 coreWebView,
        StorageFile destination)
    {
        var exportId = Guid.NewGuid();
        session.PendingImageExport = new PendingImageExport(
            exportId,
            destination,
            new CancellationTokenSource());
        session.ExportStatusMessage = null;
        host.UpdateFileMenuState(session);
        host.UpdateStatusBar(session);
        if (!session.TryPostEditorMessage(
            BridgeEventJson.Create(
                "image.exportRequested",
                new
                {
                    exportId = exportId.ToString("D"),
                    uploadUrl = $"{ImageExportPolicy.GetUploadOrigin(session.TabOrigin)}" +
                        $"/_desktop/export/{exportId:D}",
                    maxDimension = ImageExportPolicy.MaxDimension,
                    maxBytes = ImageExportPolicy.MaxPngBytes,
                    scale = ImageExportPolicy.Scale,
                    padding = ImageExportPolicy.Padding,
                })))
        {
            _ = FailImageExportAsync(
                session,
                exportId,
                DesktopResources.Get(
                    "PngExportStartFailed",
                    "The PNG export could not be started."));
            return;
        }
        _ = WatchImageExportTimeoutAsync(
            session,
            exportId,
            session.PendingImageExport.Cancellation.Token);
    }

    private void CompleteImageExport(
        DocumentSession session,
        PendingImageExport pending,
        string fileName)
    {
        if (session.PendingImageExport?.ExportId != pending.ExportId)
        {
            return;
        }

        pending.Cancellation.Cancel();
        pending.Cancellation.Dispose();
        session.PendingImageExport = null;
        session.ExportStatusMessage = DesktopResources.Format(
            "ExportedFileFormat",
            "Exported {0}",
            fileName);
        host.UpdateFileMenuState(host.ActiveSession);
        host.UpdateStatusBar(session);
#if DEBUG
        ExportCompletedForSmoke?.Invoke(pending.Destination.Path);
#endif
        _ = ClearImageExportStatusAsync(session, session.ExportStatusMessage);
    }

    private async Task WatchImageExportTimeoutAsync(
        DocumentSession session,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (sessions.Contains(session) &&
            session.PendingImageExport?.ExportId == exportId &&
            !committingExports.Contains(exportId))
        {
            await FailImageExportAsync(
                session,
                exportId,
                DesktopResources.Get(
                    "PngExportTimeout",
                    "The PNG export took too long and was cancelled."));
        }
    }

    private async Task ClearImageExportStatusAsync(
        DocumentSession session,
        string expectedMessage)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (sessions.Contains(session) &&
            string.Equals(
                session.ExportStatusMessage,
                expectedMessage,
                StringComparison.Ordinal))
        {
            session.ExportStatusMessage = null;
            host.UpdateStatusBar(session);
        }
    }

    public async Task FailImageExportAsync(
        DocumentSession session,
        Guid exportId,
        string message)
    {
        if (session.PendingImageExport is not { } pending ||
            pending.ExportId != exportId)
        {
            return;
        }
        if (committingExports.Contains(exportId))
        {
            // Commit has started and cannot be cancelled. The upload handler
            // will settle the state when the transaction reports its outcome.
            return;
        }

        // Tell the editor to abort its render/upload promise. Clearing the
        // native slot alone leaves the web busy flag set until its request
        // eventually settles (or forever if the upload is hung).
        session.TryPostEditorMessage(
            BridgeEventJson.Create(
                "image.exportCancelled",
                new
                {
                    exportId = exportId.ToString("D"),
                    reason = message,
                }));
        pending.Cancellation.Cancel();
        pending.Cancellation.Dispose();
        session.PendingImageExport = null;
        session.ExportStatusMessage = null;
        host.UpdateFileMenuState(host.ActiveSession);
        host.UpdateStatusBar(session);
#if DEBUG
        if (ExportFailedForSmoke?.Invoke(message) == true)
        {
            return;
        }
#endif
        await ShowImageExportErrorAsync(message);
    }

    public async Task ShowImageExportErrorAsync(string message)
    {
        if (host.IsDisposed || host.DialogRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = host.DialogRoot,
            Title = DesktopResources.Get(
                "PngExportErrorTitle",
                "Could not export PNG"),
            Content = message,
            CloseButtonText = DesktopResources.Get("OkButton", "OK"),
            DefaultButton = ContentDialogButton.Close,
        };
        try
        {
            await WindowModalCoordinator.For(host.OwnerWindow).RunAsync(
                async () => await dialog.ShowAsync());
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("image_export.error_dialog_failed", exception);
        }
    }

    private static string GetImageExportFailureMessage(Exception exception) =>
        exception switch
        {
            BridgeProtocolException => exception.Message,
            UnauthorizedAccessException =>
                DesktopResources.Get(
                    "PngExportAccessDenied",
                    "Excalidraw Desktop does not have permission to write the selected location."),
            IOException =>
                DesktopResources.Get(
                    "PngExportWriteFailed",
                    "The PNG could not be written. Check the destination and available disk space."),
            _ => DesktopResources.Get(
                "PngExportDestinationFailed",
                "The PNG could not be written to the selected location."),
        };
}
