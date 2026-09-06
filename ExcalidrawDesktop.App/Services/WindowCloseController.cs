using System.Diagnostics;
using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App.Services;

internal interface IWindowCloseHost
{
    bool SaveDirtyDrawingsOnClose { get; }
    Task<bool> YieldToDispatcherAsync();
    void CloseSession(DocumentSession session);
    void UpdateTabHeader(DocumentSession session);
    Task ShowImageExportErrorAsync(string message);
    Task PersistWorkspaceAsync();
    Task PruneRecoverySnapshotsAsync();
    void RecordWindowDiscarded();
    void ReleasePreservedRecovery(string recoveryId);
    WindowModalCoordinator Modals { get; }
    Task<bool> EnterCloseBarrierAsync(IReadOnlyList<DocumentSession> targets, bool closingWindow);
    void ExitCloseBarrier(IEnumerable<DocumentSession> targets);
}

/// <summary>Coordinates save, discard, and cancellation for tab and window closing.</summary>
internal sealed class WindowCloseController
{
    private readonly IWindowCloseHost host;
    private readonly IReadOnlyList<DocumentSession> sessions;
    private readonly TabView documentTabs;

    public WindowCloseController(IWindowCloseHost host,
        IReadOnlyList<DocumentSession> sessions, TabView documentTabs)
    {
        this.host = host;
        this.sessions = sessions;
        this.documentTabs = documentTabs;
    }

    private TimeSpan SaveResponseTimeout { get; set; } = TimeSpan.FromSeconds(30);
#if DEBUG
    public TimeSpan SaveResponseTimeoutForSmoke
    {
        get => SaveResponseTimeout;
        set => SaveResponseTimeout = value;
    }
    public bool SuppressSaveRequestForSmoke { get; set; }
#endif

    public void OnCloseReady(DocumentSession session, Guid requestId)
    {
        if (!sessions.Contains(session) || session.CloseRequestId != requestId)
        {
            return;
        }
        (session.WindowCloseSaveCompletion ?? session.CloseCompletion)?.TrySetResult(!session.IsDirty);
    }

    public void OnCloseCancelled(DocumentSession session, Guid requestId)
    {
        if (session.CloseRequestId == requestId)
        {
            OnCloseCancelled(session);
        }
    }

    public void OnCloseCancelled(DocumentSession session)
    {
        session.CloseRequestId = null;
        var saveCompletion = session.WindowCloseSaveCompletion;
        var completion = session.CloseCompletion;
        session.WindowCloseSaveCompletion = null;
        session.CloseCompletion = null;
        saveCompletion?.TrySetResult(false);
        completion?.TrySetResult(false);
    }

    private async Task<bool> RequestCloseSaveAsync(DocumentSession session, bool closeTab)
    {
        if (session.CloseCompletion is not null || session.WindowCloseSaveCompletion is not null)
        {
            return false;
        }
        var requestId = Guid.NewGuid();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.CloseRequestId = requestId;
        if (closeTab)
        {
            session.CloseCompletion = completion;
        }
        else
        {
            session.WindowCloseSaveCompletion = completion;
        }
        try
        {
            var shouldPost = true;
#if DEBUG
            shouldPost = !SuppressSaveRequestForSmoke;
#endif
            if (shouldPost && !session.TryPostEditorMessage(BridgeEventJson.Create(
                    "document.saveRequested", new { reason = "close", closeRequestId = requestId })))
            {
                return false;
            }
            var saved = await CloseSaveWait.UntilAsync(completion.Task, SaveResponseTimeout,
                () => session.DocumentService.IsSavePickerOpen);
            if (!saved && !completion.Task.IsCompleted)
            {
                DiagnosticLogService.Info("window.close_save_timeout", new { sessionId = session.RecoveryId });
                session.Dispatcher.CancelCloseSave(requestId);
                session.TryPostEditorMessage(BridgeEventJson.Create(
                    "document.saveCancelled", new { closeRequestId = requestId }));
            }
            if (!saved || !sessions.Contains(session) || session.IsDirty)
            {
                return false;
            }
            if (closeTab)
            {
                host.CloseSession(session);
            }
            return true;
        }
        finally
        {
            // A cancelled or timed-out request must not consume a later acknowledgement.
            if (session.CloseRequestId == requestId)
            {
                OnCloseCancelled(session);
            }
        }
    }

    public async Task<bool> RequestCloseSessionAsync(DocumentSession session)
    {
        if (session.CloseBarrierId is not null) return false;
        try
        {
            return await host.EnterCloseBarrierAsync([session], closingWindow: false) &&
                await RequestCloseSessionCoreAsync(session);
        }
        finally
        {
            host.ExitCloseBarrier([session]);
        }
    }

    private async Task<bool> RequestCloseSessionCoreAsync(DocumentSession session)
    {
        // Removing and disposing a TabViewItem while WinUI is routing the
        // pointer/click event that targeted it can invalidate the input tree.
        // Keep this deferral inside the shared close path so bulk and future
        // callers receive the same protection.
        if (!await host.YieldToDispatcherAsync())
        {
            return false;
        }

        if (!sessions.Contains(session) || session.ClosePromptOpen)
        {
            return false;
        }

        if (session.IsExporting)
        {
            await host.ShowImageExportErrorAsync(
                DesktopResources.Get(
                    "WaitForDrawingExport",
                    "Wait for the current PNG export to finish before closing this drawing."));
            return false;
        }

        if (!session.IsDirty)
        {
            host.CloseSession(session);
            return true;
        }

        session.ClosePromptOpen = true;
        documentTabs.SelectedItem = session.TabItem;
        try
        {
            var canAutoSave = host.SaveDirtyDrawingsOnClose &&
                EditorSessionController.CanRequestSave(session);
            var decision = canAutoSave
                ? CloseDecision.Save
                : await session.DocumentService.PromptToSaveBeforeCloseAsync();
            if (decision == CloseDecision.Discard)
            {
                host.CloseSession(session);
                return true;
            }

            if (decision == CloseDecision.Save && EditorSessionController.CanRequestSave(session))
            {
                return await RequestCloseSaveAsync(session, closeTab: true);
            }

            return false;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            OnCloseCancelled(session);
            return false;
        }
        finally
        {
            session.ClosePromptOpen = false;
        }
    }

    public async Task<bool> ResolveWindowCloseAsync()
    {
        if (sessions.Any(session => session.CloseBarrierId is not null)) return false;
        var targets = sessions.ToArray();
        var approved = false;
        try
        {
            approved = await host.EnterCloseBarrierAsync(targets, closingWindow: true) &&
                await ResolveWindowCloseCoreAsync();
            return approved;
        }
        finally
        {
            if (!approved)
            {
                host.ExitCloseBarrier(targets);
            }
        }
    }

    private Task<bool> PersistCloseDecisionAsync()
    {
        var versions = sessions.Select(session => (Session: session, Version: session.DocumentStateVersion,
            LoadId: session.PendingDocumentLoad?.LoadId)).ToArray();
        return CloseCommitBarrier.CompleteAsync(
            () => sessions.Count == versions.Length && versions.All(item =>
                sessions.Contains(item.Session) && item.Session.CloseBarrierId is not null &&
                item.Session.PendingDocumentLoad?.LoadId == item.LoadId &&
                item.Session.DocumentStateVersion == item.Version && !item.Session.IsDirty &&
                (!item.Session.HasUnsavedLibrary || item.Session.DiscardLibraryOnClose) &&
                !item.Session.DocumentService.IsSaving && !item.Session.IsExporting),
            host.PersistWorkspaceAsync,
            host.PruneRecoverySnapshotsAsync);
    }

    private async Task<bool> ResolveWindowCloseCoreAsync()
    {
        if (sessions.Any(session => session.IsExporting))
        {
            await host.ShowImageExportErrorAsync(
                DesktopResources.Get(
                    "WaitForWindowExports",
                    "Wait for PNG exports to finish before closing this window."));
            return false;
        }

        var dirtySessions = sessions.Where(session => session.IsDirty).ToList();
        if (dirtySessions.Count == 0)
        {
            return await PersistCloseDecisionAsync();
        }

        if (host.SaveDirtyDrawingsOnClose &&
            dirtySessions.All(EditorSessionController.CanRequestSave))
        {
            if (!await SaveAllForWindowCloseAsync(dirtySessions))
            {
                return false;
            }
            return await PersistCloseDecisionAsync();
        }

        var names = string.Join(
            Environment.NewLine,
            dirtySessions.Select(session => $"• {session.DisplayName}"));
        var reviewRequested = false;
        var reviewButton = new Button
        {
            Content = DesktopResources.Get(
                "ReviewTabsButton",
                "Review tabs individually"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(reviewButton, "ReviewTabsButton");
        AutomationProperties.SetName(
            reviewButton,
            DesktopResources.Get(
                "ReviewTabsAutomationName",
                "Review unsaved tabs individually"));
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new TextBlock
        {
            Text = DesktopResources.Format(
                "UnsavedDrawingsCountFormat",
                "{0} drawing(s) have unsaved changes:\n\n{1}",
                dirtySessions.Count,
                names),
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(reviewButton);
        var dialog = new ContentDialog
        {
            XamlRoot = documentTabs.XamlRoot,
            Title = DesktopResources.Get("UnsavedDrawingsTitle", "Unsaved drawings"),
            Content = content,
            PrimaryButtonText = DesktopResources.Get("SaveAllButton", "Save all"),
            SecondaryButtonText = DesktopResources.Get(
                "DiscardAllButton",
                "Discard all"),
            CloseButtonText = DesktopResources.Get("CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        reviewButton.Click += (_, _) =>
        {
            reviewRequested = true;
            dialog.Hide();
        };

        var result = await host.Modals.RunAsync(async () => await dialog.ShowAsync());
        if (reviewRequested)
        {
            documentTabs.SelectedItem = dirtySessions[0].TabItem;
            return false;
        }
        if (result == ContentDialogResult.Primary)
        {
            if (!await SaveAllForWindowCloseAsync(dirtySessions))
            {
                return false;
            }
            return await PersistCloseDecisionAsync();
        }
        if (result != ContentDialogResult.Secondary)
        {
            return false;
        }

        var discardedSessions = dirtySessions
            .Where(session => sessions.Contains(session) && session.IsDirty)
            .ToArray();
        foreach (var discardedSession in discardedSessions)
        {
#if DEBUG
            discardedSession.ForceDirtyForSmoke = false;
#endif
            discardedSession.IsDirty = false;
            host.ReleasePreservedRecovery(discardedSession.RecoveryId);
        }
        host.RecordWindowDiscarded();
        try
        {
            if (await PersistCloseDecisionAsync())
            {
                return true;
            }
            foreach (var discardedSession in discardedSessions)
            {
                discardedSession.IsDirty = true;
                host.UpdateTabHeader(discardedSession);
            }
            return false;
        }
        catch
        {
            foreach (var discardedSession in discardedSessions)
            {
                discardedSession.IsDirty = true;
                host.UpdateTabHeader(discardedSession);
            }
            throw;
        }
    }

    public async Task<bool> SaveAllForWindowCloseAsync(
        IEnumerable<DocumentSession> dirtySessions)
    {
        var completed = await WorkspaceTabOperations.RunSequentiallyAsync(
            dirtySessions.ToArray(),
            async session =>
            {
                if (!sessions.Contains(session) || !session.IsDirty)
                {
                    return true;
                }

                documentTabs.SelectedItem = session.TabItem;
                return await RequestSaveForWindowCloseAsync(session);
            });

        return completed && sessions.All(session => !session.IsDirty);
    }

    public async Task<bool> RequestSaveForWindowCloseAsync(
        DocumentSession session)
    {
        if (!sessions.Contains(session) || !session.IsDirty)
        {
            return true;
        }

        if (session.WindowCloseSaveCompletion is not null ||
            session.CloseCompletion is not null)
        {
            return false;
        }

        // The caller selected the tab, which starts a lazy editor
        // initialization for tabs restored from recovery; give it a bounded
        // chance to become ready before deciding the save cannot run.
        if (!EditorSessionController.CanRequestSave(session) &&
            !await AsyncWait.UntilAsync(
                () => !sessions.Contains(session) || EditorSessionController.CanRequestSave(session),
                TimeSpan.FromSeconds(10)))
        {
            return false;
        }

        if (!sessions.Contains(session) || !EditorSessionController.CanRequestSave(session))
        {
            return !session.IsDirty;
        }

        try
        {
            return await RequestCloseSaveAsync(session, closeTab: false);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return false;
        }
    }
}
