namespace ExcalidrawDesktop.Core;

public static class ImageExportPolicy
{
    public const int Scale = 2;
    public const int Padding = 10;
    public const int MaxDimension = 16_384;
    public const long MaxPngBytes = 100L * 1024 * 1024;

    public static string GetUploadOrigin(DesktopTabOrigin origin) =>
        $"https://export-{origin.SessionId:N}.excalidraw.local";

    public static string CreateSuggestedBaseName(string displayName)
    {
        var name = Path.GetFileNameWithoutExtension(displayName);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Untitled";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim()
            .TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized;
    }

    public static bool IsMatchingUpload(
        string requestUri,
        DesktopTabOrigin origin,
        Guid exportId)
    {
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) ||
            !string.Equals(
                uri.GetLeftPart(UriPartial.Authority),
                GetUploadOrigin(origin),
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return string.Equals(
            uri.AbsolutePath,
            $"/_desktop/export/{exportId:D}",
            StringComparison.OrdinalIgnoreCase);
    }
}
