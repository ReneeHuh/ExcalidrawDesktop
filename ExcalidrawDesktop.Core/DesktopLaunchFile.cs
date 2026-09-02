namespace ExcalidrawDesktop.Core;

public static class DesktopLaunchFile
{
    public static string FormatArguments(string path)
    {
        var canonicalPath = DesktopDocumentPath.Normalize(path) ??
            throw new ArgumentException("A drawing path is required.", nameof(path));
        if (!IsDrawingPath(canonicalPath))
        {
            throw new ArgumentException(
                "The launch path must identify an Excalidraw drawing.",
                nameof(path));
        }

        return $"\"{canonicalPath}\"";
    }

    public static string? ParseArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        try
        {
            var path = DesktopDocumentPath.Normalize(
                arguments.Trim().Trim('"'));
            return path is not null && IsDrawingPath(path) ? path : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsDrawingPath(string path) =>
        string.Equals(
            Path.GetExtension(path),
            ".excalidraw",
            StringComparison.OrdinalIgnoreCase);
}
