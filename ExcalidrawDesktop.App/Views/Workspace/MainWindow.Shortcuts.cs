using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using ExcalidrawDesktop.App.Services.Platform;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private void RefreshClosedItemsMenu()
    {
        ReopenClosedMenuItem.IsEnabled = !CommandsBlocked && workspaceCoordinator.ClosedItems.Count > 0;
        RecentlyClosedMenu.Items.Clear();
        RecentlyClosedMenu.IsEnabled = ReopenClosedMenuItem.IsEnabled;
        foreach (var item in workspaceCoordinator.ClosedItems)
        {
            var names = string.Join(", ", item.Window.Tabs.Select(tab => tab.DisplayName ?? DesktopResources.Get("RecoveredDrawingName", "Recovered drawing")));
            var label = item.IsWindow ? DesktopResources.Format("ClosedWindowLabelFormat", "Window: {0}", names) : names;
            var menuItem = new MenuFlyoutItem { Text = label };
            menuItem.Click += (_, _) =>
            {
                if (!CommandsBlocked) _ = workspaceCoordinator.ReopenLastClosedAsync(this, item.Id);
            };
            RecentlyClosedMenu.Items.Add(menuItem);
        }
    }

    private void DispatchWorkspaceCommand(string command)
    {
        if (CommandsBlocked) return;
        switch (command)
        {
            case "newTab": NewTabFromInput("editor_keyboard"); break;
            case "newWindow": NewWindowFromInput("editor_keyboard"); break;
            case "closeTab": CloseActiveTabFromInput("editor_keyboard"); break;
            case "closeWindow": CloseWindowFromInput("editor_keyboard"); break;
            case "reopenClosed": ReopenClosedFromInput(); break;
            case "open": OpenFromInput("editor_keyboard"); break;
            case "save": SaveFromInput(false, "editor_keyboard"); break;
            case "saveAs": SaveFromInput(true, "editor_keyboard"); break;
            case "saveAll": SaveAllFromInput("editor_keyboard"); break;
            case "nextTab":
            case "previousTab":
                if (DocumentTabs.SelectedItem is TabViewItem tab)
                    SelectAdjacentTabItem(tab, command == "nextTab");
                break;
            default:
                if (command.Length == 4 && command.StartsWith("tab", StringComparison.Ordinal) &&
                    command[3] is >= '1' and <= '9') SelectNumberedTab(command[3] - '0');
                break;
        }
    }

    private void ReopenClosedFromInput()
    {
        if (!CommandsBlocked) _ = workspaceCoordinator.ReopenLastClosedAsync(this);
    }

    private void OnReopenClosedClick(object sender, RoutedEventArgs args) => ReopenClosedFromInput();

    private void OnReopenClosedAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (workspaceCoordinator.IsRepeatedShortcut((uint)sender.Key)) return;
        ReopenClosedFromInput();
    }

    private void OnSelectNumberedTabAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (workspaceCoordinator.IsRepeatedShortcut((uint)sender.Key)) return;
        SelectNumberedTab((int)sender.Key - (int)VirtualKey.Number0);
    }

    private void SelectNumberedTab(int number)
    {
        if (CommandsBlocked) return;
        var index = number == 9 ? DocumentTabs.TabItems.Count - 1 : number - 1;
        if (index >= 0 && index < DocumentTabs.TabItems.Count) DocumentTabs.SelectedIndex = index;
    }
}
