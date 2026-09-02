using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class SessionMoveRulesTests
{
    [Fact]
    public void AllowsStableSession()
    {
        Assert.True(SessionMoveRules.CanMove(new SessionMoveState(
            false, false, false, false, false, false, false, false)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void RejectsEveryBusyState(int busyIndex)
    {
        var states = new bool[8];
        states[busyIndex] = true;
        Assert.False(SessionMoveRules.CanMove(new SessionMoveState(
            states[0],
            states[1],
            states[2],
            states[3],
            states[4],
            states[5],
            states[6],
            states[7])));
    }
}
