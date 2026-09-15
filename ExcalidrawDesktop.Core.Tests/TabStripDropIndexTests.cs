using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class TabStripDropIndexTests
{
    private static readonly TabStripSlot[] LeftToRight =
    [
        new(0, 100),
        new(100, 100),
        new(200, 100),
    ];

    // Starts decrease with logical index, as a mirrored right-to-left strip reports.
    private static readonly TabStripSlot[] RightToLeftMirrored =
    [
        new(200, 100),
        new(100, 100),
        new(0, 100),
    ];

    [Fact]
    public void EmptyStripAppends()
    {
        Assert.Equal(0, TabStripDropIndex.FromPointer(42, Array.Empty<TabStripSlot>()));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(49, 0)]
    [InlineData(51, 1)]
    [InlineData(149, 1)]
    [InlineData(151, 2)]
    [InlineData(249, 2)]
    [InlineData(251, 3)]
    [InlineData(900, 3)]
    public void InsertsBeforeFirstMidpointPastPointer(double pointerX, int expected)
    {
        Assert.Equal(expected, TabStripDropIndex.FromPointer(pointerX, LeftToRight));
    }

    [Theory]
    [InlineData(900, 0)]
    [InlineData(251, 0)]
    [InlineData(249, 1)]
    [InlineData(151, 1)]
    [InlineData(149, 2)]
    [InlineData(51, 2)]
    [InlineData(49, 3)]
    [InlineData(-10, 3)]
    public void FollowsMirroredDirection(double pointerX, int expected)
    {
        Assert.Equal(expected, TabStripDropIndex.FromPointer(pointerX, RightToLeftMirrored));
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(90, 1)]
    public void SingleTabSplitsAtMidpoint(double pointerX, int expected)
    {
        Assert.Equal(expected, TabStripDropIndex.FromPointer(pointerX, [new TabStripSlot(0, 100)]));
    }

    [Theory]
    [InlineData(20, 0)]
    [InlineData(100, 1)]
    [InlineData(200, 2)]
    public void HandlesUnequalWidths(double pointerX, int expected)
    {
        TabStripSlot[] slots = [new(0, 50), new(50, 200)];
        Assert.Equal(expected, TabStripDropIndex.FromPointer(pointerX, slots));
    }

    [Fact]
    public void ScrolledStripKeepsOrder()
    {
        TabStripSlot[] scrolled = [new(-150, 100), new(-50, 100), new(50, 100)];
        Assert.Equal(2, TabStripDropIndex.FromPointer(10, scrolled));
        Assert.Equal(0, TabStripDropIndex.FromPointer(-140, scrolled));
    }
}
