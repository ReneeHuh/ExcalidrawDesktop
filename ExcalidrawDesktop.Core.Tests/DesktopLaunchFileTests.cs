using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class DesktopLaunchFileTests
{
    [Fact]
    public void FormatAndParsePreserveDrawingPathsWithSpaces()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "Jump List Drawings",
            "Project plan.excalidraw");

        var arguments = DesktopLaunchFile.FormatArguments(path);

        Assert.Equal($"\"{DesktopDocumentPath.Normalize(path)}\"", arguments);
        Assert.Equal(DesktopDocumentPath.Normalize(path),
            DesktopLaunchFile.ParseArguments(arguments));
    }

    [Theory]
    [InlineData("")]
    [InlineData("notes.txt")]
    [InlineData("--tab-smoke")]
    public void ParseRejectsNonDrawingArguments(string arguments)
    {
        Assert.Null(DesktopLaunchFile.ParseArguments(arguments));
    }
}
