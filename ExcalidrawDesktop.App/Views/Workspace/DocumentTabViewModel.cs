using ExcalidrawDesktop.App.Services.Documents;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.App.ViewModels;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.App.Views.Workspace;

/// <summary>A display projection of a live session; it never owns or changes document state.</summary>
public sealed class DocumentTabViewModel : ObservableObject
{
    private readonly DocumentSession session;

    internal DocumentTabViewModel(DocumentSession session) => this.session = session;

    public string DisplayName => session.DisplayName;
    public string DocumentText => session.DocumentService.DocumentPath ?? session.DisplayName;
    public string HelpText => session.DocumentService.DocumentPath ??
        DesktopResources.Get("UnsavedDrawingHelpText", "Unsaved drawing");
    public string DocumentAutomationName => DesktopResources.Format(
        "StatusDrawingAutomationFormat", "Drawing: {0}", DocumentText);
    public string StatusText => GetStatus().Text;
    public bool IsStatusAction => GetStatus().Actionable;
    public string StatusActionHelpText => IsStatusAction
        ? DesktopResources.Get("StatusResolveHelpText", "Open options for resolving the file conflict.")
        : string.Empty;

    public string HeaderText
    {
        get
        {
            var text = session.IsDirty ? $"{session.DisplayName} ●" : session.DisplayName;
            return session.IsSuspended || session.IsUnloaded
                ? DesktopResources.Format(
                    "SleepingTabHeaderFormat",
                    "{0} — Sleeping",
                    text)
                : text;
        }
    }


    public string AutomationName
    {
        get
        {
            var accessibilityStates = new List<string>();
            if (session.DocumentService.DocumentPath is null)
            {
                accessibilityStates.Add(DesktopResources.Get(
                    "TabStateNewDrawing",
                    "new drawing"));
            }
            accessibilityStates.Add(session.IsDirty
                ? DesktopResources.Get("TabStateUnsaved", "unsaved changes")
                : DesktopResources.Get("TabStateSaved", "saved"));
            if (session.IsSuspended || session.IsUnloaded)
            {
                accessibilityStates.Add(DesktopResources.Get(
                    "TabStateSleeping",
                    "sleeping"));
            }
            if (session.ExternalFileState is not ExternalFileState.None)
            {
                accessibilityStates.Add(session.ExternalFileState switch
                {
                    ExternalFileState.Unavailable => DesktopResources.Get("TabStateFileUnavailable", "file unavailable"),
                    ExternalFileState.UnknownBaseline => DesktopResources.Get("TabStateFileVersionUnknown", "file version unknown"),
                    _ => DesktopResources.Get("TabStateChangedOnDisk", "changed on disk"),
                });
            }


            return DesktopResources.Format("TabAutomationNameFormat", "{0}, {1}",
                session.DisplayName, string.Join(", ", accessibilityStates));
        }
    }

    internal void Refresh() => OnPropertyChanged(string.Empty);

    private (string Text, bool Actionable) GetStatus()
    {
        var path = session.DocumentService.DocumentPath;
        string status;
        var actionable = false;
        if (session.IsExporting)
        {
            status = DesktopResources.Get("StatusExportingPng", "Exporting PNG…");
        }
        else if (session.ExportStatusMessage is { } exportStatus)
        {
            status = exportStatus;
        }
        else if (session.IsResuming || session.IsRetrying)
        {
            status = DesktopResources.Get("StatusResuming", "Resuming");
        }
        else if (session.IsUnloaded || session.IsUnloading ||
            session.IsSuspended || session.IsSuspensionChanging)
        {
            status = DesktopResources.Get("StatusSleeping", "Sleeping");
        }
        else if (session.ExternalFileState == ExternalFileState.Unavailable)
        {
            status = DesktopResources.Get("StatusFileUnavailable", "File unavailable — Retry");
            actionable = true;
        }
        else if (session.ExternalFileState == ExternalFileState.UnknownBaseline)
        {
            status = DesktopResources.Get("StatusFileBaselineUnknown", "File version unknown — Resolve");
            actionable = true;
        }
        else if (session.ExternalFileState == ExternalFileState.Modified)
        {
            status = DesktopResources.Get(
                "StatusChangedOnDisk",
                "Changed on disk — Resolve");
            actionable = true;
        }
        else if (session.ExternalFileState == ExternalFileState.Deleted)
        {
            status = DesktopResources.Get(
                "StatusFileMissing",
                "File missing — Resolve");
            actionable = true;
        }
        else if (session.ExternalFileState == ExternalFileState.Moved)
        {
            status = DesktopResources.Get(
                "StatusFileMoved",
                "File moved — Resolve");
            actionable = true;
        }
        else if (session.IsDirty && session.RecoveryFailed)
        {
            status = DesktopResources.Get("StatusRecoveryFailed", "Recovery unavailable — Save As…");
            actionable = true;
        }
        else if (session.IsDirty)
        {
            status = session.RecoveryUpdatedAt is { } recoveredAt
                ? DesktopResources.Format(
                    "StatusUnsavedRecoveredFormat",
                    "Unsaved • recovered {0}",
                    recoveredAt.ToLocalTime().ToString("t"))
                : DesktopResources.Get("StatusUnsaved", "Unsaved changes");
        }
        else
        {
            status = path is null
                ? DesktopResources.Get("StatusNewDrawing", "New drawing")
                : DesktopResources.Get("StatusSaved", "Saved");
        }

        return (status, actionable);
    }
}
