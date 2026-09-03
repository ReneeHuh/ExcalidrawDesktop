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
    bool SaveDirtyDrawingsOnClose,
    bool SuspendInactiveTabs,
    bool UnloadInactiveTabs,
    string Language)
{
    public static DesktopPreferences Default { get; } = new(
        DesktopThemePreference.System,
        ReopenSavedTabs: true,
        SaveDirtyDrawingsOnClose: false,
        SuspendInactiveTabs: true,
        UnloadInactiveTabs: true,
        ExcalidrawDesktop.Core.DesktopLanguages.SystemPreference);
}
