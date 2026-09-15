using ExcalidrawDesktop.App.Services.Logging;
using ExcalidrawDesktop.App.Services.Documents;
using ExcalidrawDesktop.App.Services.Editor;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Services.Workspace;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.App.Views.Workspace;
using ExcalidrawDesktop.App.Views.Workspace.UserControls;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App;

// Native view wiring for this feature; document workflows stay in the controllers.
public sealed partial class MainWindow
{
    internal void SelectSession(DocumentSession session)
    {
        if (!sessions.Contains(session))
        {
            return;
        }

        DocumentTabs.SelectedItem = session.TabItem;
    }

    private DocumentSession CreateTab(bool select = true)
    {
        var displayName = NextUntitledName();
        var content = new DocumentTabContent();
        var documentService = new DocumentService(this, content);
        var session = new DocumentSession(
            DesktopTabOrigin.Create(),
            displayName,
            content,
            documentService);
        DocumentTabPresentation.Bind(session.TabItem, session.ViewModel);
        AttachWindowSessionHost(session);

        session.Content.Visibility = Visibility.Collapsed;
        EditorHost.Children.Add(session.Content);
        sessions.Add(session);
        DocumentTabs.TabItems.Add(session.TabItem);
        UpdateTabHeader(session);

        if (select)
        {
            DocumentTabs.SelectedIndex = DocumentTabs.TabItems.Count - 1;
            foreach (var openSession in sessions)
            {
                openSession.Content.Visibility = ReferenceEquals(
                    openSession,
                    session)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        return session;
    }

    private void AttachWindowSessionHost(DocumentSession session)
    {
        session.DetachWindowHandlers?.Invoke();
        session.DetachWindowHandlers = null;
        session.DocumentService.AttachHost(this, session.Content);
        session.DocumentService.IsPathOwnedByAnotherSession = path =>
            workspaceCoordinator.IsPathOwnedByAnotherSession(session, path);
        session.DocumentService.TryReservePath = path =>
            workspaceCoordinator.TryReservePath(path, session, out var reservation) ? reservation : null;
        session.Dispatcher = new BridgeDispatcher(
            session.DocumentService,
            () => editorSessions.MarkReady(session),
            requestId => windowClose.OnCloseReady(session, requestId),
            isDirty => OnDirtyChanged(session, isDirty),
            () => OnDocumentCreated(session),
            fileName => OnDocumentOpened(session, fileName),
            () => OnDocumentRecovered(session),
            content => documents.OnRecoverySnapshotReceivedAsync(session, content),
            () => _ = documents.CheckExternalFileStateAsync(session, showPrompt: true),
            requestId => windowClose.OnCloseCancelled(session, requestId),
            () => NewTabFromInput("editor"),
            () => documents.RequestOpenDocumentAsync(session),
            () => QueueCloseSession(session),
            next => SelectAdjacentTab(session, next),
            (exportId, message) =>
                _ = imageExports.FailImageExportAsync(session, exportId, message),
            (langCode, direction) =>
                OnEditorLanguageApplied(session, langCode, direction),
            documentLoadFailed: () => editorSessions.HandleLoadFailure(session),
            documentSaved: fileName => OnDocumentSaved(session, fileName))
        {
            IsCloseSavePending = requestId => session.CloseRequestId == requestId,
        };
        EventHandler retryRequested = (_, _) => _ = editorSessions.RetryAsync(session);
        EventHandler closeRequested = (_, _) => QueueCloseSession(session);
        EventHandler webView2HelpRequested = (_, _) =>
            _ = OpenExternalUriAsync(WebView2HelpUri);
        session.Content.RetryRequested += retryRequested;
        session.Content.CloseRequested += closeRequested;
        session.Content.WebView2HelpRequested += webView2HelpRequested;
        session.TabItem.ContextFlyout = CreateTabContextFlyout(session);
        session.DetachWindowHandlers = () =>
        {
            session.Content.RetryRequested -= retryRequested;
            session.Content.CloseRequested -= closeRequested;
            session.Content.WebView2HelpRequested -= webView2HelpRequested;
            session.TabItem.ContextFlyout = null;
            session.DocumentService.IsPathOwnedByAnotherSession = null;
            session.DocumentService.TryReservePath = null;
        };
        ConfigureEditorDropTarget(session, session.Content.Editor);
    }

    private void ConfigureEditorDropTarget(
        DocumentSession session,
        WebView2 editor)
    {
        session.DetachEditorHandlers?.Invoke();
        session.Content.IsEnabled = session.CloseBarrierId is null;
        DragEventHandler dragOver = OnFileDragOver;
        DragEventHandler drop = OnFileDrop;
        editor.AllowDrop = true;
        editor.AddHandler(
            UIElement.DragOverEvent,
            dragOver,
            handledEventsToo: true);
        editor.AddHandler(
            UIElement.DropEvent,
            drop,
            handledEventsToo: true);
        session.DetachEditorHandlers = () =>
        {
            editor.RemoveHandler(UIElement.DragOverEvent, dragOver);
            editor.RemoveHandler(UIElement.DropEvent, drop);
        };
    }

    private void OnTabCloseRequested(
        TabView sender,
        TabViewTabCloseRequestedEventArgs args)
    {
        LogAction("Close tab requested", "tab", FindSession(args.Tab));
        if (settingsTabItem is not null && ReferenceEquals(args.Tab, settingsTabItem))
        {
            QueueHideSettingsPage();
            return;
        }

        if (FindSession(args.Tab) is { } session)
        {
            QueueCloseSession(session);
        }
    }

    private void QueueCloseSession(DocumentSession session)
    {
        if (CommandsBlocked) return;
        _ = RequestCloseSessionAndReportAsync(session);
    }

    private async Task<bool> RequestCloseSessionAndReportAsync(
        DocumentSession session)
    {
        try
        {
            return await windowClose.RequestCloseSessionAsync(session);
        }
        catch (Exception exception)
        {
            AppLogger.Error($"[MainWindow] Tab close failed (SessionId={session.RecoveryId}, " +
                $"IsDirty={session.IsDirty}, " +
                $"IsReady={session.IsReady}, " +
                $"IsSuspended={session.IsSuspended}, " +
                $"IsUnloaded={session.IsUnloaded})", exception);
            return false;
        }
    }

    private void OnTabItemsChanged(
        TabView sender,
        Windows.Foundation.Collections.IVectorChangedEventArgs args)
    {
        UpdateTabWidths();
        var orderedSessions = GetOrderedSessions();
        if (orderedSessions.Count == sessions.Count &&
            !orderedSessions.SequenceEqual(sessions))
        {
            sessions.Clear();
            sessions.AddRange(orderedSessions);
            QueuePersistWorkspace();
        }
    }

    private void OnTabSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        LogAction("Tab selected", "tab");
        var settingsSelected = settingsTabItem is not null &&
            ReferenceEquals(DocumentTabs.SelectedItem, settingsTabItem);
        settingsPageVisible = settingsSelected;
        AppSettingsPage.Visibility = settingsSelected
            ? Visibility.Visible
            : Visibility.Collapsed;
        EditorHost.Visibility = settingsSelected
            ? Visibility.Collapsed
            : Visibility.Visible;
        var active = ActiveSession;
        var now = DateTimeOffset.UtcNow;
        foreach (var session in sessions)
        {
            var isActive = ReferenceEquals(session, active);
            if (isActive)
            {
                if (session.IsUnloaded)
                {
                    _ = editorSessions.WakeAsync(session);
                }
                else
                {
                    editorSessions.Resume(session);
                }
                session.InactiveSince = null;
            }
            else
            {
                session.InactiveSince ??= now;
            }
            session.Content.Visibility = isActive
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        UpdateWindowTitle();
        if (settingsSelected)
        {
            AppSettingsPage.Focus(FocusState.Programmatic);
            QueuePersistWorkspace();
            return;
        }

        if (isWindowReady && active is { } activeSession)
        {
            lastDocumentSession = activeSession;
            if (!activeSession.IsUnloaded &&
                !activeSession.IsRestoringFromHibernation)
            {
                activeSession.Content.Editor.Focus(FocusState.Programmatic);
                _ = editorSessions.InitializeAsync(activeSession);
            }
            _ = documents.CheckExternalFileStateAsync(activeSession, showPrompt: true);
        }
        QueuePersistWorkspace();
    }

    internal DocumentSessionTransfer? DetachSessionForMove(
        DocumentSession session)
    {
        if (!CanMoveSession(session))
        {
            return null;
        }

        var sourceIndex = DocumentTabs.TabItems.IndexOf(session.TabItem);
        if (sourceIndex < 0)
        {
            return null;
        }

        session.IsMoving = true;
        var coreWebView = session.CoreWebView;
        session.DetachExternalFileWatcher();
        session.DetachWindowHandlers?.Invoke();
        session.DetachWindowHandlers = null;
        session.DetachEditorHandlers?.Invoke();
        session.DetachEditorHandlers = null;
        EditorSessionController.DetachWebView(session);
        sessions.Remove(session);
        DocumentTabs.TabItems.Remove(session.TabItem);
        EditorHost.Children.Remove(session.Content);
        if (ReferenceEquals(lastDocumentSession, session))
        {
            lastDocumentSession = sessions.FirstOrDefault();
        }
        UpdateWindowTitle();
        return new DocumentSessionTransfer(session, coreWebView, sourceIndex);
    }

    internal void AttachMovedSession(
        DocumentSessionTransfer transfer,
        int? index = null)
    {
        var session = transfer.Session;
        if (sessions.Contains(session))
        {
            throw new InvalidOperationException(
                "The drawing session is already attached to this window.");
        }

        var insertIndex = Math.Clamp(
            index ?? DocumentTabs.TabItems.Count,
            0,
            DocumentTabs.TabItems.Count);
        sessions.Insert(Math.Min(insertIndex, sessions.Count), session);
        EditorHost.Children.Add(session.Content);
        DocumentTabs.TabItems.Insert(insertIndex, session.TabItem);
        AttachWindowSessionHost(session);
        if (transfer.CoreWebView is { } coreWebView)
        {
            editorSessions.AttachWebView(session, coreWebView);
        }
        if (session.DocumentService.DocumentPath is { } path)
        {
            documents.WatchExternalFile(session, path);
        }

        session.IsMoving = false;
        session.Content.Visibility = Visibility.Visible;
        DocumentTabs.SelectedItem = session.TabItem;
        foreach (var openSession in sessions)
        {
            openSession.Content.Visibility = ReferenceEquals(openSession, session)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        UpdateTabHeader(session);
        UpdateWindowTitle();
        QueuePersistWorkspace();
    }

    internal void CloseIfEmptyAfterMove()
    {
        if (IsClosed || sessions.Count != 0)
        {
            return;
        }

        allowClose = true;
        Close();
    }

    private bool CanMoveSession(DocumentSession session) =>
        !CommandsBlocked && !WindowModalCoordinator.For(this).IsBusy &&
        sessions.Contains(session) &&
        session.IsReady &&
        !session.IsRetrying &&
        !session.IsSuspended &&
        !session.IsUnloaded &&
        !session.IsRestoringFromHibernation &&
        session.PendingDocumentLoad is null &&
        SessionMoveRules.CanMove(new SessionMoveState(
            session.IsMoving,
            session.IsInitializing,
            session.IsBridgeDispatching,
            session.CloseCompletion is not null ||
                session.WindowCloseSaveCompletion is not null,
            session.ClosePromptOpen ||
                session.ExternalConflictPromptOpen ||
                windowClosePromptOpen ||
                documents.IsPickerActive,
            session.IsSuspensionChanging,
            session.IsResuming,
            session.IsUnloading,
            session.IsExporting));

    /// <summary>Whether a tab dragged from another window may be dropped into this one.</summary>
    private bool CanReceiveSession() =>
        !CommandsBlocked &&
        !WindowModalCoordinator.For(this).IsBusy &&
        !documents.IsPickerActive;

    private void CloseSession(DocumentSession session, bool discardRecovery = true)
    {
        if (!sessions.Remove(session))
        {
            return;
        }

        DocumentTabs.TabItems.Remove(session.TabItem);
        EditorHost.Children.Remove(session.Content);
        if (discardRecovery) _ = documents.DeleteRecoverySnapshotAsync(session);
        session.Dispose();
        if (ReferenceEquals(lastDocumentSession, session))
        {
            lastDocumentSession = sessions.FirstOrDefault();
        }

        if (sessions.Count == 0 && !allowClose)
        {
            CreateTab();
        }

        UpdateWindowTitle();
        QueuePersistWorkspace();
    }

    private void SelectAdjacentTab(DocumentSession source, bool next)
    {
        SelectAdjacentTabItem(source.TabItem, next);
    }

    private void SelectAdjacentTabItem(TabViewItem source, bool next)
    {
        var tabs = DocumentTabs.TabItems.OfType<TabViewItem>().ToArray();
        if (tabs.Length < 2)
        {
            return;
        }

        var sourceIndex = Array.IndexOf(tabs, source);
        if (sourceIndex < 0)
        {
            return;
        }

        var destinationIndex = next
            ? (sourceIndex + 1) % tabs.Length
            : (sourceIndex - 1 + tabs.Length) % tabs.Length;
        DocumentTabs.SelectedItem = tabs[destinationIndex];
    }

    private IReadOnlyList<DocumentSession> GetOrderedSessions() =>
        DocumentTabs.TabItems
            .OfType<TabViewItem>()
            .Select(FindSession)
            .OfType<DocumentSession>()
            .ToArray();

    private void SynchronizeSessionOrder()
    {
        var orderedSessions = GetOrderedSessions();
        if (orderedSessions.Count != sessions.Count)
        {
            return;
        }

        sessions.Clear();
        sessions.AddRange(orderedSessions);
        QueuePersistWorkspace();
    }

    private DocumentSession? FindSession(TabViewItem tab) =>
        sessions.FirstOrDefault(session => ReferenceEquals(session.TabItem, tab));

    private string NextUntitledName()
    {
        untitledSequence++;
        var untitled = DesktopResources.Get("UntitledName", "Untitled");
        return untitledSequence == 1
            ? untitled
            : DesktopResources.Format(
                "UntitledNumberedFormat",
                "{0} {1}",
                untitled,
                untitledSequence);
    }
}
