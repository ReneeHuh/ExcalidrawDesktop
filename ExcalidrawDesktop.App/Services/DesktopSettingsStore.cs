using ExcalidrawDesktop.App.Models;
using Windows.Storage;

namespace ExcalidrawDesktop.App.Services;

internal sealed class DesktopSettingsStore
{
    private const string ThemeKey = "Settings.Theme";
    private const string ReopenSavedTabsKey = "Settings.ReopenSavedTabs";
    private const string SuspendInactiveTabsKey = "Settings.SuspendInactiveTabs";
    private const string UnloadInactiveTabsKey = "Settings.UnloadInactiveTabs";
    private const string LanguageKey = "Settings.Language";

    private readonly ApplicationDataContainer values =
        ApplicationData.Current.LocalSettings;

    public DesktopPreferences Load()
    {
        var defaults = DesktopPreferences.Default;
        var theme = values.Values[ThemeKey] is string themeValue &&
            Enum.TryParse<DesktopThemePreference>(themeValue, ignoreCase: true, out var parsed)
                ? parsed
                : defaults.Theme;
        return new DesktopPreferences(
            theme,
            ReadBoolean(ReopenSavedTabsKey, defaults.ReopenSavedTabs),
            ReadBoolean(SuspendInactiveTabsKey, defaults.SuspendInactiveTabs),
            ReadBoolean(UnloadInactiveTabsKey, defaults.UnloadInactiveTabs),
            ExcalidrawDesktop.Core.DesktopLanguages.NormalizePreference(
                values.Values[LanguageKey] as string));
    }

    public void Save(DesktopPreferences preferences)
    {
        values.Values[ThemeKey] = preferences.Theme.ToString();
        values.Values[ReopenSavedTabsKey] = preferences.ReopenSavedTabs;
        values.Values[SuspendInactiveTabsKey] = preferences.SuspendInactiveTabs;
        values.Values[UnloadInactiveTabsKey] = preferences.UnloadInactiveTabs;
        values.Values[LanguageKey] = preferences.Language;
    }

    private bool ReadBoolean(string key, bool fallback) =>
        values.Values[key] is bool value ? value : fallback;
}
