namespace ExcalidrawDesktop.Core;

public static class DesktopDocumentPath
{
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        if (path.IndexOf('\0') >= 0) return null;
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (IOException) { return null; }
    }

    public static bool Equals(string? left, string? right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        return normalizedLeft is not null &&
            normalizedRight is not null &&
            string.Equals(
                normalizedLeft,
                normalizedRight,
                StringComparison.OrdinalIgnoreCase);
    }
}
