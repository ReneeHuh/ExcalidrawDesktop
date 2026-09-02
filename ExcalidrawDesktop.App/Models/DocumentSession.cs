using ExcalidrawDesktop.Core;
using ExcalidrawDesktop.App.Controls;
using ExcalidrawDesktop.App.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

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

    public string DisplayName { get; set; }

    public bool IsDirty { get; set; }

    public bool IsReady { get; set; }

    public bool IsInitializing { get; set; }

    public bool IsSuspended { get; set; }

    public bool IsSuspensionChanging { get; set; }

    public bool IsResuming { get; set; }

    public bool IsUnloaded { get; set; }

    public bool IsUnloading { get; set; }

    public bool IsRestoringFromHibernation { get; set; }

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

    public PendingEditorLoad? PendingDocumentLoad { get; set; }

    public string HeaderText
    {
        get
        {
            var text = IsDirty ? $"{DisplayName} ●" : DisplayName;
            return IsSuspended || IsUnloaded ? $"{text} — Sleeping" : text;
        }
    }

    public void Dispose()
    {
        Content.CloseEditor();
    }
}

internal sealed record PendingEditorLoad(
    string FileName,
    string Content,
    bool IsRecovery);
