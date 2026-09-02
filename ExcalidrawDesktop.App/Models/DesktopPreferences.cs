namespace ExcalidrawDesktop.App.Models;

internal enum DesktopThemePreference
{
    System,
    Light,
    Dark,
}

internal sealed record DesktopPreferences(
    DesktopThemePreference Theme,
    bool ReopenSavedTabs,
    bool SuspendInactiveTabs,
    bool UnloadInactiveTabs)
{
    public static DesktopPreferences Default { get; } = new(
        DesktopThemePreference.System,
        ReopenSavedTabs: true,
        SuspendInactiveTabs: true,
        UnloadInactiveTabs: true);
}
