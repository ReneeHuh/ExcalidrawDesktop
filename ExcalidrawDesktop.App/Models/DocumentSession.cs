using ExcalidrawDesktop.Core;
using ExcalidrawDesktop.App.Controls;
using ExcalidrawDesktop.App.Services;
using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;

namespace ExcalidrawDesktop.App.Models;

internal sealed class DocumentSession : IDisposable
{
    public DocumentSession(
        DesktopTabOrigin tabOrigin,
        string displayName,
        DocumentTabContent content,
        DocumentService documentService)
    {
        TabOrigin = tabOrigin;
        DisplayName = displayName;
        Content = content;
        DocumentService = documentService;
        TabItem = new TabViewItem
        {
            Header = displayName,
            IsClosable = true,
        };
    }

    public DesktopTabOrigin TabOrigin { get; }

    public string RecoveryId { get; set; } = Guid.NewGuid().ToString("N");

    public SemaphoreSlim RecoveryGate { get; } = new(1, 1);

    public DateTimeOffset? RecoveryUpdatedAt { get; set; }

    public DateTimeOffset? InactiveSince { get; set; }

    public ExternalFileState ExternalFileState { get; set; }

    public bool ExternalConflictPromptOpen { get; set; }

    public FileSystemWatcher? ExternalFileWatcher { get; set; }

    public FileSystemEventHandler? ExternalFileChangedHandler { get; set; }

    public RenamedEventHandler? ExternalFileRenamedHandler { get; set; }

    public string DisplayName { get; set; }

    public bool IsDirty { get; set; }
#if DEBUG
    public bool ForceDirtyForSmoke { get; set; }
#endif

    public bool IsReady { get; set; }

    public bool IsInitializing { get; set; }

    public bool IsSuspended { get; set; }

    public bool IsSuspensionChanging { get; set; }

    public bool IsResuming { get; set; }

    public bool IsUnloaded { get; set; }

    public bool IsUnloading { get; set; }

    public bool IsRestoringFromHibernation { get; set; }

    public bool IsMoving { get; set; }

    public int BridgeDispatchDepth { get; set; }

    public bool IsBridgeDispatching => BridgeDispatchDepth > 0;

    public bool IsExporting => PendingImageExport is not null;

    public PendingImageExport? PendingImageExport { get; set; }

    public string? ExportStatusMessage { get; set; }

    public string? HibernatedContent { get; set; }

    public string? LastLifecycleFailure { get; set; }

    public bool CloseAfterSave { get; set; }

    public bool ClosePromptOpen { get; set; }

    public TaskCompletionSource<bool>? CloseCompletion { get; set; }

    public TaskCompletionSource<bool>? WindowCloseSaveCompletion { get; set; }

    public DocumentTabContent Content { get; }

    public DocumentService DocumentService { get; }

    public TabViewItem TabItem { get; }

    public BridgeDispatcher Dispatcher { get; set; } = null!;

    public CoreWebView2? CoreWebView { get; set; }

    public Action? DetachWebViewHandlers { get; set; }

    public Action? DetachEditorHandlers { get; set; }

    public Action? DetachWindowHandlers { get; set; }

    public PendingEditorLoad? PendingDocumentLoad { get; set; }

    public string HeaderText
    {
        get
        {
            var text = IsDirty ? $"{DisplayName} ●" : DisplayName;
            return IsSuspended || IsUnloaded
                ? DesktopResources.Format(
                    "SleepingTabHeaderFormat",
                    "{0} — Sleeping",
                    text)
                : text;
        }
    }

    public void Dispose()
    {
        PendingImageExport?.Cancellation.Cancel();
        PendingImageExport?.Cancellation.Dispose();
        PendingImageExport = null;

        try
        {
            DetachExternalFileWatcher();
        }
        catch (Exception exception)
        {
            LastLifecycleFailure = exception.Message;
            Debug.WriteLine($"File-watcher cleanup failed: {exception}");
        }

        var detachWebViewHandlers = DetachWebViewHandlers;
        DetachWebViewHandlers = null;
        CoreWebView = null;
        try
        {
            detachWebViewHandlers?.Invoke();
        }
        catch (Exception exception)
        {
            LastLifecycleFailure = exception.Message;
            Debug.WriteLine($"WebView event cleanup failed: {exception}");
        }

        var detachEditorHandlers = DetachEditorHandlers;
        DetachEditorHandlers = null;
        try
        {
            detachEditorHandlers?.Invoke();
        }
        catch (Exception exception)
        {
            LastLifecycleFailure = exception.Message;
            Debug.WriteLine($"Editor input cleanup failed: {exception}");
        }

        var detachWindowHandlers = DetachWindowHandlers;
        DetachWindowHandlers = null;
        try
        {
            detachWindowHandlers?.Invoke();
        }
        catch (Exception exception)
        {
            LastLifecycleFailure = exception.Message;
            Debug.WriteLine($"Window session-host cleanup failed: {exception}");
        }

        Content.CloseEditor();
        Content.ClearActionHandlers();
    }

    public void DetachExternalFileWatcher()
    {
        if (ExternalFileWatcher is not { } watcher)
        {
            return;
        }

        var changed = ExternalFileChangedHandler;
        var renamed = ExternalFileRenamedHandler;
        ExternalFileWatcher = null;
        ExternalFileChangedHandler = null;
        ExternalFileRenamedHandler = null;
        try
        {
            watcher.EnableRaisingEvents = false;
            if (changed is not null)
            {
                watcher.Changed -= changed;
                watcher.Deleted -= changed;
            }
            if (renamed is not null)
            {
                watcher.Renamed -= renamed;
            }
        }
        finally
        {
            watcher.Dispose();
        }
    }
}

internal sealed record PendingEditorLoad(
    string FileName,
    string Content,
    bool IsRecovery);

internal sealed record PendingImageExport(
    Guid ExportId,
    StorageFile Destination,
    CancellationTokenSource Cancellation);
