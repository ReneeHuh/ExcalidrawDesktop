using ExcalidrawDesktop.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;

namespace ExcalidrawDesktop.App.Pages;

internal sealed class PreferencesChangedEventArgs(DesktopPreferences preferences) : EventArgs
{
    public DesktopPreferences Preferences { get; } = preferences;
}

public sealed partial class SettingsPage : Page
{
    private bool isLoading;

    internal event EventHandler<PreferencesChangedEventArgs>? PreferencesChanged;

    public SettingsPage()
    {
        InitializeComponent();
        var version = Package.Current.Id.Version;
        VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    internal void LoadPreferences(DesktopPreferences preferences)
    {
        isLoading = true;
        ThemePicker.SelectedIndex = preferences.Theme switch
        {
            DesktopThemePreference.Light => 1,
            DesktopThemePreference.Dark => 2,
            _ => 0,
        };
        ReopenSavedTabsToggle.IsOn = preferences.ReopenSavedTabs;
        SuspendInactiveTabsToggle.IsOn = preferences.SuspendInactiveTabs;
        UnloadInactiveTabsToggle.IsOn = preferences.UnloadInactiveTabs;
        isLoading = false;
    }

    private void OnPreferenceChanged(object sender, object args)
    {
        if (isLoading || ThemePicker.SelectedItem is not ComboBoxItem themeItem)
        {
            return;
        }

        var theme = Enum.TryParse<DesktopThemePreference>(
            themeItem.Tag?.ToString(),
            ignoreCase: true,
            out var parsed)
                ? parsed
                : DesktopThemePreference.System;
        PreferencesChanged?.Invoke(
            this,
            new PreferencesChangedEventArgs(new DesktopPreferences(
                theme,
                ReopenSavedTabsToggle.IsOn,
                SuspendInactiveTabsToggle.IsOn,
                UnloadInactiveTabsToggle.IsOn)));
    }
}
