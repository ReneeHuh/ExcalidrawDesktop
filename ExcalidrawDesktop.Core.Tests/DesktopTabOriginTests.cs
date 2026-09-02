using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Core.Tests;

public sealed class DesktopTabOriginTests
{
    private static readonly Guid SessionId =
        Guid.Parse("8f3ca308-b496-4495-9dc7-18cdbe8a43f2");

    [Fact]
    public void Create_ProducesAUniqueDnsSafeHttpsOrigin()
    {
        var origin = DesktopTabOrigin.Create(SessionId);

        Assert.Equal(
            "tab-8f3ca308b49644959dc718cdbe8a43f2.excalidraw.local",
            origin.Host);
        Assert.Equal($"https://{origin.Host}", origin.Origin);
        Assert.Equal($"{origin.Origin}/index.html", origin.EntryPoint);
    }

    [Theory]
    [InlineData("https://tab-8f3ca308b49644959dc718cdbe8a43f2.excalidraw.local/index.html", true)]
    [InlineData("https://TAB-8F3CA308B49644959DC718CDBE8A43F2.EXCALIDRAW.LOCAL/assets/app.js", true)]
    [InlineData("http://tab-8f3ca308b49644959dc718cdbe8a43f2.excalidraw.local/index.html", false)]
    [InlineData("https://tab-11111111111111111111111111111111.excalidraw.local/index.html", false)]
    [InlineData("https://tab-8f3ca308b49644959dc718cdbe8a43f2.excalidraw.local.example.com/", false)]
    public void Matches_RequiresTheExactAssignedHttpsOrigin(string value, bool expected)
    {
        var origin = DesktopTabOrigin.Create(SessionId);

        Assert.Equal(expected, origin.Matches(value));
    }
}
