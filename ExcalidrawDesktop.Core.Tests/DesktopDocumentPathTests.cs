using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class DesktopDocumentPathTests
{
    [Fact]
    public void EqualsTreatsWindowsCaseAndRelativeSegmentsAsTheSameFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "Drawings", "Scene.excalidraw");
        var equivalent = Path.Combine(
            Path.GetTempPath(),
            "drawings",
            "folder",
            "..",
            "scene.excalidraw");

        Assert.True(DesktopDocumentPath.Equals(path, equivalent));
    }

    [Fact]
    public void EqualsRejectsMissingAndDifferentPaths()
    {
        Assert.False(DesktopDocumentPath.Equals(null, null));
        Assert.False(DesktopDocumentPath.Equals("drawing.excalidraw", "other.excalidraw"));
    }
}
