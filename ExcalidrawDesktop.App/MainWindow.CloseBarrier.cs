using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private bool CommandsBlocked => resourcesDisposed || windowClosePromptOpen ||
        workspaceCoordinator.IsExiting || sessions.Any(session => session.CloseBarrierId is not null);

    private async Task<bool> EnterCloseBarrierAsync(IReadOnlyList<DocumentSession> targets, bool closingWindow)
    {
        if (restoringWorkspace || WindowModalCoordinator.For(this).IsBusy ||
            targets.Any(session => session.CloseBarrierId is not null ||
                session.IsMoving || session.IsUnloading))
        {
            return false;
        }
        if (targets.Any(session => session.IsExporting))
        {
            await imageExports.ShowImageExportErrorAsync(closingWindow
                ? DesktopResources.Get("WaitForWindowExports", "Wait for PNG exports to finish before closing this window.")
                : DesktopResources.Get("WaitForDrawingExport", "Wait for the current PNG export to finish before closing this drawing."));
            return false;
        }

        foreach (var session in targets)
        {
            session.CloseBarrierId = Guid.NewGuid();
            session.DiscardLibraryOnClose = false;
            session.CloseBarrierCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (session.Content.HasEditor)
            {
                session.Content.IsEnabled = false;
            }
        }

        try
        {
            foreach (var session in targets)
            {
                // A never-mounted clean tab has no editable scene to freeze. Dirty
                // recovery tabs are initialized so close-save can capture their scene.
                if (!session.IsReady && !session.IsInitializing && !session.IsDirty)
                {
                    continue;
                }
                if (session.IsSuspended)
                {
                    editorSessions.Resume(session);
                }
                if (session.IsUnloaded)
                {
                    await editorSessions.WakeAsync(session);
                }
                else if (!session.IsReady)
                {
                    await editorSessions.InitializeAsync(session);
                }

                if (!await AsyncWait.UntilAsync(
                    () => !sessions.Contains(session) || session.LastLifecycleFailure is not null ||
                        (session.IsReady && session.PendingDocumentLoad is null && !session.IsResuming),
                    TimeSpan.FromSeconds(30)) || !sessions.Contains(session))
                {
                    await ShowCloseWaitMessageAsync();
                    return false;
                }
                if (session.LastLifecycleFailure is not null)
                {
                    // A failed page cannot acknowledge. Stop its callbacks before
                    // presenting the existing save/discard/cancel recovery decision.
                    EditorSessionController.DetachWebView(session);
                    continue;
                }

                session.Content.IsEnabled = false;
                if (!session.TryPostEditorMessage(BridgeEventJson.Create(
                    "document.closeBarrierRequested",
                    new { barrierId = session.CloseBarrierId, locked = true })) ||
                    !await CloseSaveWait.UntilAsync(session.CloseBarrierCompletion!.Task,
                        TimeSpan.FromSeconds(30), () => session.DocumentService.IsSavePickerOpen))
                {
                    if (session.LastLifecycleFailure is not null)
                    {
                        EditorSessionController.DetachWebView(session);
                        continue;
                    }
                    await ShowCloseWaitMessageAsync();
                    return false;
                }
            }
            var unsavedLibraries = targets.Where(session => session.HasUnsavedLibrary).ToArray();
            if (unsavedLibraries.Length > 0)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = DocumentTabs.XamlRoot,
                    Title = DesktopResources.Get("UnsavedLibraryTitle", "Unsaved library changes"),
                    Content = DesktopResources.Get("UnsavedLibraryCloseContent", "Library changes could not be saved. Discard these library changes and continue closing, or cancel to retry saving them. Drawing changes will be handled separately."),
                    PrimaryButtonText = DesktopResources.Get("DiscardLibraryButton", "Discard library changes"),
                    CloseButtonText = DesktopResources.Get("CancelButton", "Cancel"),
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await WindowModalCoordinator.For(this).RunAsync(async () => await dialog.ShowAsync()) != ContentDialogResult.Primary)
                    return false;
                foreach (var session in unsavedLibraries) session.DiscardLibraryOnClose = true;
            }
            return true;
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("window.close_barrier_failed", exception);
            return false;
        }
    }

    private Task ShowCloseWaitMessageAsync() => WindowModalCoordinator.For(this).ShowMessageAsync(
        DesktopResources.Get("CloseNotReadyTitle", "Could not close drawing"),
        DesktopResources.Get("CloseNotReadyContent", "The editor did not finish preparing to close. Try again, or retry the editor if it has stopped."));

    private void ExitCloseBarrier(IEnumerable<DocumentSession> targets)
    {
        foreach (var session in targets)
        {
            if (session.CloseBarrierId is not { } barrierId)
            {
                continue;
            }
            session.TryPostEditorMessage(BridgeEventJson.Create(
                "document.closeBarrierRequested", new { barrierId, locked = false }));
            session.CloseBarrierId = null;
            session.DiscardLibraryOnClose = false;
            session.CloseBarrierCompletion?.TrySetResult(false);
            session.CloseBarrierCompletion = null;
            if (sessions.Contains(session) && session.Content.HasEditor)
            {
                session.Content.IsEnabled = true;
            }
        }
    }
}
