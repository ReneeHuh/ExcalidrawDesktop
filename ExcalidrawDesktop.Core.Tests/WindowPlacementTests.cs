using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class WindowPlacementTests
{
    private static readonly WorkspaceWindowBounds WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void KeepsBoundsThatAlreadyFit()
    {
        var requested = new WorkspaceWindowBounds(100, 50, 800, 600);
        Assert.Equal(requested, WindowPlacement.ClampToWorkArea(requested, WorkArea));
    }

    [Fact]
    public void ShrinksOversizedBoundsToTheWorkArea()
    {
        var clamped = WindowPlacement.ClampToWorkArea(new(0, 0, 3000, 2000), WorkArea);
        Assert.Equal(new WorkspaceWindowBounds(0, 0, 1920, 1040), clamped);
    }

    [Fact]
    public void MovesOffscreenBoundsInside()
    {
        Assert.Equal(
            new WorkspaceWindowBounds(1120, 440, 800, 600),
            WindowPlacement.ClampToWorkArea(new(1500, 900, 800, 600), WorkArea));
        Assert.Equal(
            new WorkspaceWindowBounds(0, 0, 800, 600),
            WindowPlacement.ClampToWorkArea(new(-300, -200, 800, 600), WorkArea));
    }

    [Fact]
    public void HonoursWorkAreasWithNegativeOrigins()
    {
        var secondary = new WorkspaceWindowBounds(-1920, -200, 1920, 1040);
        Assert.Equal(
            new WorkspaceWindowBounds(-1920, -200, 800, 600),
            WindowPlacement.ClampToWorkArea(new(-2500, -900, 800, 600), secondary));
    }

    [Fact]
    public void DroppedTabWindowPutsPointerOnTheTabStrip()
    {
        var bounds = WindowPlacement.ForDroppedTab(
            pointerX: 700, pointerY: 300,
            sourceWidth: 1000, sourceHeight: 700, sourceMaximized: false,
            pointerInsetX: 160, pointerInsetY: 24,
            WorkArea);
        Assert.Equal(new WorkspaceWindowBounds(540, 276, 1000, 700), bounds);
    }

    [Fact]
    public void DroppedTabWindowStaysInsideTheWorkArea()
    {
        var bounds = WindowPlacement.ForDroppedTab(
            pointerX: 1900, pointerY: 1030,
            sourceWidth: 1000, sourceHeight: 700, sourceMaximized: false,
            pointerInsetX: 160, pointerInsetY: 24,
            WorkArea);
        Assert.Equal(new WorkspaceWindowBounds(920, 340, 1000, 700), bounds);
    }

    [Fact]
    public void MaximizedSourceOpensAWindowCoveringMostOfTheWorkArea()
    {
        var bounds = WindowPlacement.ForDroppedTab(
            pointerX: 960, pointerY: 20,
            sourceWidth: 1920, sourceHeight: 1040, sourceMaximized: true,
            pointerInsetX: 160, pointerInsetY: 24,
            WorkArea);
        Assert.Equal(new WorkspaceWindowBounds(384, 0, 1536, 832), bounds);
    }
}
