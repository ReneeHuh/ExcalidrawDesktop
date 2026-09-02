using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class WorkspaceTabOperationsTests
{
    private sealed class Tab;

    [Fact]
    public void AdjacentFollowsVisibleOrderAndWraps()
    {
        var first = new Tab();
        var second = new Tab();
        var third = new Tab();
        var ordered = new[] { third, first, second };

        Assert.Same(second, WorkspaceTabOperations.Adjacent(ordered, first, next: true));
        Assert.Same(third, WorkspaceTabOperations.Adjacent(ordered, second, next: true));
        Assert.Same(second, WorkspaceTabOperations.Adjacent(ordered, third, next: false));
    }

    [Fact]
    public void BulkCloseTargetsRespectVisibleOrder()
    {
        var first = new Tab();
        var second = new Tab();
        var third = new Tab();
        var ordered = new[] { third, first, second };

        Assert.Equal(
            new[] { third, second },
            WorkspaceTabOperations.OtherTabs(ordered, first));
        Assert.Equal(
            new[] { first, second },
            WorkspaceTabOperations.TabsToRight(ordered, third));
        Assert.Empty(WorkspaceTabOperations.TabsToRight(ordered, second));
    }

    [Fact]
    public async Task SequentialOperationStopsAtTheFirstCancellation()
    {
        var visited = new List<int>();

        var completed = await WorkspaceTabOperations.RunSequentiallyAsync(
            new[] { 1, 2, 3 },
            tab =>
            {
                visited.Add(tab);
                return Task.FromResult(tab != 2);
            });

        Assert.False(completed);
        Assert.Equal(new[] { 1, 2 }, visited);
    }
}
