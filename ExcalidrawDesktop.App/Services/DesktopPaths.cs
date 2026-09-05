namespace ExcalidrawDesktop.App.Services;

internal static class DesktopPaths
{
    internal const string DataRootEnvironmentVariable =
        "EXCALIDRAW_DESKTOP_DATA_ROOT";

    public static string DataRoot { get; } = ResolveDataRoot();

    /// <summary>
    /// Set when <see cref="DataRootEnvironmentVariable"/> was present but could
    /// not be used, so the default location was chosen instead. The diagnostics
    /// log reports it once startup logging is available.
    /// </summary>
    public static string? DataRootOverrideError { get; private set; }

    public static string SettingsPath => Path.Combine(DataRoot, "settings.json");

    public static string WorkspaceStatePath =>
        Path.Combine(DataRoot, "workspace-state.json");

    public static string DiagnosticsDirectory =>
        Path.Combine(DataRoot, "Diagnostics");

    public static string WebView2DataDirectory =>
        Path.Combine(DataRoot, "WebView2");

    public static string FileAssociationStampPath =>
        Path.Combine(DataRoot, "file-association.stamp");

    public static string GetRecoveryDirectory(string workspaceStatePath) =>
        Path.Combine(
            Path.GetDirectoryName(workspaceStatePath)!,
            $"{Path.GetFileNameWithoutExtension(workspaceStatePath)}.recovery");

    private static string ResolveDataRoot()
    {
        var overriddenRoot = Environment.GetEnvironmentVariable(
            DataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overriddenRoot))
        {
            try
            {
                var fullPath = Path.GetFullPath(overriddenRoot.Trim().Trim('"'));
                if (Path.IsPathRooted(fullPath))
                {
                    return fullPath;
                }

                DataRootOverrideError =
                    $"{DataRootEnvironmentVariable} must be an absolute path.";
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                DataRootOverrideError =
                    $"{DataRootEnvironmentVariable} is not a valid path: {exception.Message}";
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExcalidrawDesktop");
    }
}
