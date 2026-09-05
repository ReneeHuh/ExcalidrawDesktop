namespace ExcalidrawDesktop.App.Services;

internal static class DesktopPaths
{
    internal const string DataRootEnvironmentVariable =
        "EXCALIDRAW_DESKTOP_DATA_ROOT";

    public static string DataRoot { get; } = ResolveDataRoot();

    public static string SettingsPath => Path.Combine(DataRoot, "settings.json");

    public static string WorkspaceStatePath =>
        Path.Combine(DataRoot, "workspace-state.json");

    public static string DiagnosticsDirectory =>
        Path.Combine(DataRoot, "Diagnostics");

    public static string WebView2DataDirectory =>
        Path.Combine(DataRoot, "WebView2");

    private static string ResolveDataRoot()
    {
        var overriddenRoot = Environment.GetEnvironmentVariable(
            DataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overriddenRoot))
        {
            return Path.GetFullPath(overriddenRoot);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExcalidrawDesktop");
    }
}
