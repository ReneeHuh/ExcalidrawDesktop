using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services.Editor;
using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.App.Services.Workspace;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.App.Views.Settings;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App;

// Native view wiring for this feature; document workflows stay in the controllers.
public sealed partial class MainWindow
{
    private void OnPreferencesChanged(
        object? sender,
        PreferencesChangedEventArgs args)
    {
        if (!CommandsBlocked) workspaceCoordinator.UpdatePreferences(args.Preferences);
    }

    private void OnSettingsPersistenceRetry(object? sender, EventArgs args) =>
        workspaceCoordinator.UpdatePreferences(desktopPreferences);

    internal void ApplySharedPreferences(DesktopPreferences preferences)
    {
        desktopPreferences = preferences;
        var persistenceFailed = workspaceCoordinator.LastSettingsPersistenceResult?.Outcome ==
            ApplicationWorkspaceCoordinator.SettingsPersistenceOutcome.AppliedInMemoryOnly;
        if (!persistenceFailed) ViewModel.SettingsVM.ClearSettingsPersistenceFailure();
        ViewModel.SettingsVM.LoadPreferences(
            desktopPreferences,
            workspaceCoordinator.EffectiveLanguage,
            workspaceCoordinator.StartupLanguagePreference);
        if (persistenceFailed)
            ViewModel.SettingsVM.ShowSettingsPersistenceFailure();
        ApplyDesktopTheme();

        if (!preferences.SuspendInactiveTabs)
        {
            foreach (var session in sessions.Where(session => session.IsSuspended))
            {
                editorSessions.Resume(session);
            }
        }
    }

    private void ApplyDesktopTheme()
    {
        var requestedTheme = desktopPreferences.Theme switch
        {
            DesktopThemePreference.Light => ElementTheme.Light,
            DesktopThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        MainLayout.RequestedTheme = requestedTheme;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyTitleBarTheme();
        SendEditorThemeToAllSessions();
    }

    private void ApplyTitleBarTheme()
    {
        AppWindow.TitleBar.PreferredTheme = MainLayout.ActualTheme == ElementTheme.Dark
            ? TitleBarTheme.Dark
            : TitleBarTheme.Light;
    }

    private void SendEditorThemeToAllSessions()
    {
        foreach (var session in sessions)
        {
            SendEditorTheme(session);
        }
    }

    private void SendEditorTheme(DocumentSession session)
    {
        if (!EditorSessionController.CanPostMessage(session))
        {
            return;
        }

        TryPostEditorMessage(
            session,
            BridgeEventJson.Create(
                "app.themeChanged",
                new { theme = MainLayout.ActualTheme == ElementTheme.Dark ? "dark" : "light" }));
    }

    private void SendEditorLanguage(DocumentSession session)
    {
        if (!EditorSessionController.CanPostMessage(session))
        {
            return;
        }

        var language = workspaceCoordinator.EffectiveLanguage;
        TryPostEditorMessage(
            session,
            BridgeEventJson.Create(
                "app.languageChanged",
                new
                {
                    langCode = language.ExcalidrawCode,
                    direction = language.Direction,
                }));
    }

    private void OnSettingsClick(object sender, RoutedEventArgs args)
    {
        LogAction("settings.open", "menu");
        ShowSettingsPage();
    }

    private void OnSettingsButtonClick(object sender, RoutedEventArgs args)
    {
        LogAction("settings.open", "button");
        ShowSettingsPage();
    }

    private void ShowSettingsPage()
    {
        if (CommandsBlocked) return;
        ViewModel.SettingsVM.LoadPreferences(
            desktopPreferences,
            workspaceCoordinator.EffectiveLanguage,
            workspaceCoordinator.StartupLanguagePreference);
        if (settingsTabItem is null)
        {
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            header.Children.Add(new FontIcon
            {
                Glyph = "\uE713",
                FontSize = 14,
            });
            header.Children.Add(new TextBlock
            {
                Text = DesktopResources.Get("SettingsTabTitle", "Settings"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            settingsTabItem = new TabViewItem
            {
                Header = header,
                IsClosable = true,
                CanDrag = false,
            };
            AutomationProperties.SetName(
                settingsTabItem,
                DesktopResources.Get("SettingsTabTitle", "Settings"));
            AutomationProperties.SetAutomationId(settingsTabItem, "SettingsTab");
            DocumentTabs.TabItems.Add(settingsTabItem);
        }

        DocumentTabs.SelectedItem = settingsTabItem;
    }

    private void HideSettingsPage()
    {
        if (settingsTabItem is null)
        {
            return;
        }

        var tab = settingsTabItem;
        settingsTabItem = null;
        settingsPageVisible = false;
        AppSettingsPage.Visibility = Visibility.Collapsed;
        EditorHost.Visibility = Visibility.Visible;
        DocumentTabs.TabItems.Remove(tab);
        var target = lastDocumentSession is not null &&
            sessions.Contains(lastDocumentSession)
                ? lastDocumentSession
                : sessions.FirstOrDefault();
        if (target is not null)
        {
            DocumentTabs.SelectedItem = target.TabItem;
        }
        UpdateWindowTitle();
    }

    private void QueueHideSettingsPage() =>
        DispatcherQueue.TryEnqueue(HideSettingsPage);
}
