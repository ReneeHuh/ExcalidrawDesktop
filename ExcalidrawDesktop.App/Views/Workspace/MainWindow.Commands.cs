using ExcalidrawDesktop.App.Services.Logging;
using ExcalidrawDesktop.App.Services.Documents;
using ExcalidrawDesktop.App.Services.Editor;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Services.Workspace;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace ExcalidrawDesktop.App;

// Native view wiring for this feature; document workflows stay in the controllers.
public sealed partial class MainWindow
{
    private void OnFileDragOver(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(StandardDataFormats.StorageItems))
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Copy;
        args.DragUIOverride.Caption = DesktopResources.Get(
            "DragOpenCaption",
            "Open drawing tabs");
        args.DragUIOverride.IsCaptionVisible = true;
        args.Handled = true;
    }

    private async void OnFileDrop(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        args.Handled = true;
        try
        {
            var items = await args.DataView.GetStorageItemsAsync();
            var paths = DesktopDropPaths.SelectSupported(
                items.OfType<StorageFile>().Select(file => file.Path));
            QueueActivatedFiles(paths);
        }
        catch (Exception exception)
        {
            AppLogger.Error("[MainWindow] OnFileDrop failed", exception);
            await documents.ShowOpenErrorAsync(DesktopResources.Get(
                "DroppedDrawingsOpenFailed",
                "The dropped drawings could not be opened."));
        }
    }

    private void OnAddTabButtonClick(TabView sender, object args)
    {
        if (CommandsBlocked) return;
        LogAction("New tab requested", "tab_button");
        CreateTab();
    }

    private void OnFileNewTabClick(object sender, RoutedEventArgs args) =>
        NewTabFromInput("menu");

    private void OnFileNewWindowClick(object sender, RoutedEventArgs args) =>
        NewWindowFromInput("menu");

    private void OnFileOpenClick(object sender, RoutedEventArgs args) =>
        OpenFromInput("menu");

    private void OnFileSaveClick(object sender, RoutedEventArgs args) =>
        SaveFromInput(saveAs: false, "menu");

    private void OnFileSaveAsClick(object sender, RoutedEventArgs args) =>
        SaveFromInput(saveAs: true, "menu");

    // Menu items and keyboard accelerators share these so behaviour cannot
    // drift between the two input paths; only the logged source differs.

    private void NewTabFromInput(string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction("New tab requested", inputSource);
        CreateTab();
    }

    private void NewWindowFromInput(string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction("New window requested", inputSource);
        workspaceCoordinator.CreateWindow();
    }

    private void OpenFromInput(string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction("Open drawing requested", inputSource);
        var session = ActiveSession ?? lastDocumentSession ?? sessions.FirstOrDefault() ?? CreateTab();
        _ = documents.RequestOpenDocumentAsync(session);
    }

    private void SaveFromInput(bool saveAs, string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction(saveAs ? "Save As requested" : "Save requested", inputSource);
        RequestSaveFromFileMenu(saveAs);
    }

    private void SaveAllFromInput(string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction("Save all requested", inputSource);
        _ = SaveAllFromFileMenuAsync();
    }

    private void CloseActiveTabFromInput(string inputSource)
    {
        if (CommandsBlocked) return;
        LogAction("Close tab requested", inputSource);
        if (settingsTabItem is not null &&
            ReferenceEquals(DocumentTabs.SelectedItem, settingsTabItem))
        {
            QueueHideSettingsPage();
            return;
        }

        if (ActiveSession is { } session)
        {
            QueueCloseSession(session);
        }
    }

    private void CloseWindowFromInput(string inputSource)
    {
        LogAction("Close window requested", inputSource);
        Close();
    }

    private async void OnFileExportPngClick(object sender, RoutedEventArgs args)
    {
        if (CommandsBlocked) return;
        LogAction("PNG export requested", "menu");
        await imageExports.ExportActiveSessionAsPngAsync();
    }

    private void RequestSaveFromFileMenu(bool saveAs)
    {
        if (CommandsBlocked || ActiveSession is not { } session ||
            !EditorSessionController.CanRequestSave(session))
        {
            return;
        }

        session.TryPostEditorMessage(
            BridgeEventJson.Create(
                "document.saveRequested",
                new { reason = saveAs ? "saveAs" : "save" }));
    }

    private void OnFileSaveAllClick(object sender, RoutedEventArgs args) =>
        SaveAllFromInput("menu");

    private async Task SaveAllFromFileMenuAsync()
    {
        if (CommandsBlocked) return;
        var dirtySessions = sessions.Where(session => session.IsDirty).ToArray();
        if (dirtySessions.Length == 0)
        {
            return;
        }

        var originallyActive = ActiveSession;
        await windowClose.SaveAllForWindowCloseAsync(dirtySessions);
        if (originallyActive is not null && sessions.Contains(originallyActive))
        {
            DocumentTabs.SelectedItem = originallyActive.TabItem;
        }
    }

    private void OnFileCloseTabClick(object sender, RoutedEventArgs args) =>
        CloseActiveTabFromInput("menu");

    private void OnFileCloseWindowClick(object sender, RoutedEventArgs args) =>
        CloseWindowFromInput("menu");

    private void OnFileExitClick(object sender, RoutedEventArgs args)
    {
        LogAction("Exit requested", "menu");
        _ = workspaceCoordinator.RequestExitAsync();
    }

    private void OnNewTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NewTabFromInput("keyboard");
    }

    private void OnNewWindowAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NewWindowFromInput("keyboard");
    }

    private void OnCloseTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CloseActiveTabFromInput("keyboard");
    }

    private void OnOpenAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenFromInput("keyboard");
    }

    private void OnSaveAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SaveFromInput(saveAs: false, "keyboard");
    }

    private void OnSaveAsAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SaveFromInput(saveAs: true, "keyboard");
    }

    private void OnCloseWindowAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CloseWindowFromInput("keyboard");
    }

    private void OnSaveAllAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SaveAllFromInput("keyboard");
    }

    private void OnNextTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        LogAction("Next tab requested", "keyboard");
        if (DocumentTabs.SelectedItem is TabViewItem tab)
        {
            SelectAdjacentTabItem(tab, next: true);
        }
    }

    private void OnPreviousTabAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        LogAction("Previous tab requested", "keyboard");
        if (DocumentTabs.SelectedItem is TabViewItem tab)
        {
            SelectAdjacentTabItem(tab, next: false);
        }
    }

    private MenuFlyout CreateTabContextFlyout(DocumentSession session)
    {
        var flyout = new MenuFlyout();
        var newTab = new MenuFlyoutItem
        {
            Text = DesktopResources.Get("ContextNewTab", "New Tab"),
        };
        newTab.Click += (_, _) => NewTabFromInput("context_menu");

        var copyPath = new MenuFlyoutItem
        {
            Text = DesktopResources.Get("ContextCopyPath", "Copy Path"),
        };
        AutomationProperties.SetAutomationId(copyPath, "CopyTabPath");
        copyPath.Click += (_, _) => _ = CopyDocumentPathAsync(session);

        var reveal = new MenuFlyoutItem
        {
            Text = DesktopResources.Get(
                "ContextRevealInExplorer",
                "Reveal in Explorer"),
        };
        AutomationProperties.SetAutomationId(reveal, "RevealTabInExplorer");
        reveal.Click += (_, _) => _ = RevealDocumentInExplorerAsync(session);

        var moveToNewWindow = new MenuFlyoutItem
        {
            Text = DesktopResources.Get(
                "ContextMoveToNewWindow",
                "Move Tab to New Window"),
        };
        AutomationProperties.SetAutomationId(
            moveToNewWindow,
            "MoveTabToNewWindow");
        moveToNewWindow.Click += (_, _) =>
            workspaceCoordinator.MoveSessionToNewWindow(this, session);

        var close = new MenuFlyoutItem
        {
            Text = DesktopResources.Get("ContextCloseTab", "Close Tab"),
        };
        close.Click += (_, _) => QueueCloseSession(session);

        var closeOthers = new MenuFlyoutItem
        {
            Text = DesktopResources.Get(
                "ContextCloseOtherTabs",
                "Close Other Tabs"),
        };
        closeOthers.Click += (_, _) => _ = CloseSessionsAsync(
            WorkspaceTabOperations.OtherTabs(GetOrderedSessions(), session));

        var closeRight = new MenuFlyoutItem
        {
            Text = DesktopResources.Get(
                "ContextCloseTabsRight",
                "Close Tabs to the Right"),
        };
        closeRight.Click += (_, _) => _ = CloseSessionsAsync(
            WorkspaceTabOperations.TabsToRight(GetOrderedSessions(), session));

        flyout.Opening += (_, _) =>
        {
            var path = session.DocumentService.DocumentPath;
            copyPath.IsEnabled = path is not null;
            reveal.IsEnabled = path is not null && File.Exists(path);
            moveToNewWindow.IsEnabled = CanMoveSession(session);
        };

        flyout.Items.Add(newTab);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(copyPath);
        flyout.Items.Add(reveal);
        flyout.Items.Add(moveToNewWindow);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(close);
        flyout.Items.Add(closeOthers);
        flyout.Items.Add(closeRight);
        return flyout;
    }

    private async Task CopyDocumentPathAsync(DocumentSession session)
    {
        if (session.DocumentService.DocumentPath is not { } path)
        {
            return;
        }

        try
        {
            var data = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy,
            };
            data.SetText(path);
            Clipboard.SetContent(data);
            Clipboard.Flush();
        }
        catch (Exception exception)
        {
            AppLogger.Error("[MainWindow] CopyDocumentPathAsync failed", exception);
            await ShowFileLocationErrorAsync(
                DesktopResources.Get(
                    "DrawingPathCopyFailed",
                    "The drawing path could not be copied."));
        }
    }

    private async Task RevealDocumentInExplorerAsync(DocumentSession session)
    {
        if (session.DocumentService.DocumentPath is not { } path)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!File.Exists(path) || string.IsNullOrWhiteSpace(directory))
            {
                await ShowFileLocationErrorAsync(
                    DesktopResources.Get(
                        "DrawingLocationMissing",
                        "The drawing is no longer at its saved location."));
                return;
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            var options = new FolderLauncherOptions();
            options.ItemsToSelect.Add(file);
            if (!await Launcher.LaunchFolderAsync(folder, options))
            {
                await ShowFileLocationErrorAsync(
                    DesktopResources.Get(
                        "ExplorerShowDrawingFailed",
                        "File Explorer could not show this drawing."));
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("[MainWindow] RevealDocumentInExplorerAsync failed", exception);
            await ShowFileLocationErrorAsync(
                DesktopResources.Get(
                    "DrawingLocationMissing",
                    "The drawing is no longer at its saved location."));
        }
    }

    private Task ShowFileLocationErrorAsync(string message) =>
        WindowModalCoordinator.For(this).ShowMessageAsync(
            DesktopResources.Get("FileLocationUnavailableTitle", "File location unavailable"), message);

    private async Task CloseSessionsAsync(IEnumerable<DocumentSession> targets)
    {
        if (CommandsBlocked) return;
        foreach (var session in targets.ToArray())
        {
            if (sessions.Contains(session) &&
                !await windowClose.RequestCloseSessionAsync(session))
            {
                return;
            }
        }
    }

    private async void OnStatusActionClick(object sender, RoutedEventArgs args)
    {
        if (CommandsBlocked) return;
        if (ActiveSession is { IsDirty: true, RecoveryFailed: true, ExternalFileState: ExternalFileState.None })
        {
            RequestSaveFromFileMenu(saveAs: true);
            return;
        }
        if (!settingsPageVisible &&
            ActiveSession is { ExternalFileState: not ExternalFileState.None } session)
        {
            await documents.ShowExternalFileConflictAsync(session);
        }
    }

    private static async Task OpenExternalUriAsync(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != "mailto"))
        {
            return;
        }

        try
        {
            if (!await Launcher.LaunchUriAsync(uri))
                AppLogger.Warning("[MainWindow] External URI not launched");
        }
        catch (Exception exception)
        {
            AppLogger.Error("[MainWindow] External URI launch failed", exception);
        }
    }
}
