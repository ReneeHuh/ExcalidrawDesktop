using ExcalidrawDesktop.App.Services.Documents;
using ExcalidrawDesktop.App.Services.Editor;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.App.Views.Settings;
using ExcalidrawDesktop.App.Views.Workspace;

namespace ExcalidrawDesktop.App.ViewModels;

/// <summary>Long-lived UI state for one window. Application-wide services remain in the workspace coordinator.</summary>
public sealed class MainViewModel : ObservableObject
{
    private string windowTitle = DesktopResources.Get("StartingWindowTitle", "Excalidraw Desktop — Starting…");
    private DocumentTabViewModel? activeTab;
    private bool canSave;
    private bool canExport;
    private bool canSaveAll;
    private bool canCloseTab;
    private bool isStatusBarVisible;

    public SettingsViewModel SettingsVM { get; } = new();
    public DocumentTabViewModel? ActiveTab
    {
        get => activeTab;
        private set => SetProperty(ref activeTab, value);
    }
    public string WindowTitle
    {
        get => windowTitle;
        internal set => SetProperty(ref windowTitle, value);
    }
    public bool CanSave
    {
        get => canSave;
        private set => SetProperty(ref canSave, value);
    }
    public bool CanExport
    {
        get => canExport;
        private set => SetProperty(ref canExport, value);
    }
    public bool CanSaveAll
    {
        get => canSaveAll;
        private set => SetProperty(ref canSaveAll, value);
    }
    public bool CanCloseTab
    {
        get => canCloseTab;
        private set => SetProperty(ref canCloseTab, value);
    }
    public bool IsStatusBarVisible
    {
        get => isStatusBarVisible;
        private set => SetProperty(ref isStatusBarVisible, value);
    }

    internal void RefreshStatus(DocumentSession? session, bool settingsPageVisible)
    {
        ActiveTab = session?.ViewModel;
        ActiveTab?.Refresh();
        IsStatusBarVisible = !settingsPageVisible && session is not null;
    }

    internal void RefreshTitle(DocumentSession? active, bool settingsPageVisible)
    {
        if (settingsPageVisible)
        {
            WindowTitle = DesktopResources.Get(
                "SettingsWindowTitle",
                "Settings — Excalidraw Desktop");
            return;
        }
        if (active is null || !active.IsReady)
        {
            WindowTitle = DesktopResources.Get(
                "StartingWindowTitle",
                "Excalidraw Desktop — Starting…");
            return;
        }

        var dirtyMarker = active.IsDirty ? " ●" : string.Empty;
        WindowTitle = active.DocumentService.DocumentPath is null
            ? DesktopResources.Format(
                "ApplicationWindowTitleFormat",
                "Excalidraw Desktop{0}",
                dirtyMarker)
            : DesktopResources.Format(
                "DrawingWindowTitleFormat",
                "{0}{1} — Excalidraw Desktop",
                active.DisplayName,
                dirtyMarker);
        if (active.ExternalFileState is not ExternalFileState.None)
        {
            WindowTitle += DesktopResources.Get(
                "ChangedOnDiskTitleSuffix",
                " — Changed on disk");
        }
    }

    internal void RefreshCommands(DocumentSession? active, bool settingsPageVisible, IReadOnlyList<DocumentSession> sessions)
    {
        var canUseEditor = !settingsPageVisible && active is
        {
            IsReady: true,
            IsRetrying: false,
            PendingDocumentLoad: null,
            IsResuming: false,
            IsRestoringFromHibernation: false,
            CoreWebView: not null,
        };
        CanSave = canUseEditor;
        CanExport = canUseEditor && active?.IsExporting != true;
        CanSaveAll = !settingsPageVisible && sessions.Any(session =>
            session.IsDirty &&
            EditorSessionController.CanRequestSave(session));
        CanCloseTab = settingsPageVisible || active is not null;
    }

}
