using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class DesktopDropPathsTests
{
    [Fact]
    public void SelectSupportedKeepsDistinctExcalidrawFilesInDropOrder()
    {
        var first = Path.Combine(Path.GetTempPath(), "Board.excalidraw");
        var duplicate = Path.Combine(Path.GetTempPath(), ".", "board.EXCALIDRAW");
        var second = Path.Combine(Path.GetTempPath(), "Notes.EXCALIDRAW");

        var selected = DesktopDropPaths.SelectSupported([
            first,
            null,
            " ",
            duplicate,
            Path.Combine(Path.GetTempPath(), "readme.txt"),
            second,
        ]);

        Assert.Equal([
            DesktopDocumentPath.Normalize(first)!,
            DesktopDocumentPath.Normalize(second)!,
        ], selected);
    }

    [Fact]
    public void SelectSupportedRejectsNullCollection()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DesktopDropPaths.SelectSupported(null!));
    }
}
