namespace ExcalidrawDesktop.Core;

public sealed record DesktopTabOrigin(
    Guid SessionId,
    string Host,
    string Origin,
    string EntryPoint)
{
    public static DesktopTabOrigin Create() => Create(Guid.NewGuid());

    public static DesktopTabOrigin Create(Guid sessionId)
    {
        var host = $"tab-{sessionId:N}.excalidraw.local";
        var origin = $"https://{host}";
        return new DesktopTabOrigin(
            sessionId,
            host,
            origin,
            $"{origin}/index.html");
    }

    public bool Matches(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        string.Equals(uri.IdnHost, Host, StringComparison.OrdinalIgnoreCase);
}
