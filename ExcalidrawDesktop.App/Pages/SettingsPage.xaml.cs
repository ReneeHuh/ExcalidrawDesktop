using ExcalidrawDesktop.App.Models;
using ExcalidrawDesktop.App.Services;
using ExcalidrawDesktop.Core;
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
        LanguagePicker.Items.Add(new ComboBoxItem
        {
            Content = DesktopResources.Get(
                "SystemLanguageOption",
                "Use system language"),
            Tag = DesktopLanguages.SystemPreference,
        });
        foreach (var language in DesktopLanguages.Supported)
        {
            LanguagePicker.Items.Add(new ComboBoxItem
            {
                Content = language.NativeName,
                Tag = language.PreferenceTag,
            });
        }
        var version = Package.Current.Id.Version;
        VersionText.Text = DesktopResources.Format(
            "VersionFormat",
            "Version {0}.{1}.{2}.{3}",
            version.Major,
            version.Minor,
            version.Build,
            version.Revision);
    }

    internal void LoadPreferences(
        DesktopPreferences preferences,
        DesktopLanguage effectiveLanguage,
        string startupLanguagePreference)
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
        var language = DesktopLanguages.NormalizePreference(preferences.Language);
        LanguagePicker.SelectedItem = LanguagePicker.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag?.ToString(),
                language,
                StringComparison.OrdinalIgnoreCase)) ??
            LanguagePicker.Items[0];
        LanguageRestartInfoBar.IsOpen = !string.Equals(
            language,
            DesktopLanguages.NormalizePreference(startupLanguagePreference),
            StringComparison.OrdinalIgnoreCase);
        LanguageRestartInfoBar.Message = DesktopResources.Format(
            "LanguageRestartMessageFormat",
            "The application is currently using {0}. Restart Excalidraw Desktop to apply the selected language. Unsaved drawings will not be closed automatically.",
            effectiveLanguage.NativeName);
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
                UnloadInactiveTabsToggle.IsOn,
                (LanguagePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ??
                    DesktopLanguages.SystemPreference)));
    }
}
