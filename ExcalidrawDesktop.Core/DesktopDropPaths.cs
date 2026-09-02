namespace ExcalidrawDesktop.Core;

public static class DesktopDropPaths
{
    public static IReadOnlyList<string> SelectSupported(
        IEnumerable<string?> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var supported = new List<string>();
        foreach (var path in paths)
        {
            string? canonicalPath;
            try
            {
                canonicalPath = DesktopDocumentPath.Normalize(path);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                continue;
            }

            if (canonicalPath is null ||
                !string.Equals(
                    Path.GetExtension(canonicalPath),
                    ".excalidraw",
                    StringComparison.OrdinalIgnoreCase) ||
                supported.Contains(canonicalPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            supported.Add(canonicalPath);
        }

        return supported;
    }
}
