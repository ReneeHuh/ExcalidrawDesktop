using ExcalidrawDesktop.App.Services.Platform;

namespace ExcalidrawDesktop.App.Services.Logging;

internal static class LogPrivacy
{
    private static readonly string[] RedactedRoots = BuildRedactedRoots();

    private static string[] BuildRedactedRoots()
    {
        string Folder(Environment.SpecialFolder folder)
        {
            try
            {
                return Environment.GetFolderPath(folder);
            }
            catch (Exception exception) when (
                exception is ArgumentException or PlatformNotSupportedException)
            {
                return string.Empty;
            }
        }

        return
        [
            Folder(Environment.SpecialFolder.UserProfile),
            Folder(Environment.SpecialFolder.LocalApplicationData),
        ];
    }

    public static string Redact(string value)
    {
        // Longest roots first so a nested root is masked before its parent.
        var dataRootIsProfileRelative = RedactedRoots.Any(root =>
            !string.IsNullOrEmpty(root) &&
            DesktopPaths.DataRoot.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        var replacements = new (string Root, string Token)[]
        {
            (dataRootIsProfileRelative ? string.Empty : DesktopPaths.DataRoot,
                "%EXCALIDRAW_DATA_ROOT%"),
            (RedactedRoots[1], "%LOCALAPPDATA%"),
            (RedactedRoots[0], "%USERPROFILE%"),
        };
        foreach (var (root, token) in replacements.OrderByDescending(pair => pair.Root.Length))
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            value = value.Replace(root, token, StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }

}
