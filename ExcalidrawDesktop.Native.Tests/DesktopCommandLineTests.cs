using ExcalidrawDesktop.App.Services.Platform;
using ExcalidrawDesktop.Core;
using Xunit;

namespace ExcalidrawDesktop.Native.Tests;

public sealed class DesktopCommandLineTests
{
    [Fact]
    public void RedirectedActivationSeparatesExecutableAndMultipleQuotedUnicodeDrawingPaths()
    {
        var arguments = DesktopCommandLine.ParseArguments(
            "\"C:\\Installed App\\ExcalidrawDesktop.exe\" \"C:\\Drawings\\Project plan.excalidraw\" \"C:\\Drawings\\日本.excalidraw\"");
        Assert.Equal(3, arguments.Count);
        Assert.Equal([@"C:\Drawings\Project plan.excalidraw", @"C:\Drawings\日本.excalidraw"],
            DesktopDropPaths.SelectSupported(arguments));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyActivationHasNoPaths(string? arguments) =>
        Assert.Empty(DesktopCommandLine.ParseArguments(arguments));

    [Fact]
    public void SingleQuotedPathWithoutExecutableIsAlsoSupported()
    {
        Assert.Equal([@"C:\Drawings\Project plan.excalidraw"],
            DesktopCommandLine.ParseArguments("\"C:\\Drawings\\Project plan.excalidraw\""));
    }
}
