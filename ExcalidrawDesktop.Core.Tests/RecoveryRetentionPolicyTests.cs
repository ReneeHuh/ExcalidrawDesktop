using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class RecoveryRetentionPolicyTests
{
    [Fact]
    public void SeededIdsSurviveCyclesUntilExplicitRelease()
    {
        var id = Guid.NewGuid().ToString("N");
        var policy = new RecoveryRetentionPolicy();
        policy.Seed([id]);
        Assert.Contains(id, policy.GetRetained([]));
        Assert.Contains(id, policy.GetRetained([]));
        policy.Release(id);
        Assert.DoesNotContain(id, policy.GetRetained([]));
    }

    [Fact]
    public void DiscoveryFailureSkipsPruningAndLiveIdsRemainRetained()
    {
        var policy = new RecoveryRetentionPolicy();
        policy.MarkDiscoveryFailed();
        Assert.True(policy.ShouldSkipPrune);
        var live = Guid.NewGuid().ToString("N");
        Assert.Contains(live, policy.GetRetained([live]));
    }
}
