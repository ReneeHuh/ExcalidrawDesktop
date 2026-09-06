using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class LibraryRepairSessionTests
{
    [Fact]
    public async Task DeclineSuppressesLaterTabsButAllowsExplicitRetryAndNewRevisions()
    {
        var session = new LibraryRepairSession();
        var current = new LibraryLoadResult("corrupt", "[]", "bad1");
        var prompts = 0;
        Task<LibraryLoadResult> Load() => Task.FromResult(current);
        Task<bool> Decline() { prompts++; return Task.FromResult(false); }
        Task<LibraryLoadResult> Repair(string _) => throw new InvalidOperationException("Repair was not approved.");
        Assert.Equal("unavailable", (await session.LoadAsync(Load, Repair, Decline)).Status);
        Assert.Equal("unavailable", (await session.LoadAsync(Load, Repair, Decline)).Status);
        Assert.Equal(1, prompts);
        await session.LoadAsync(Load, Repair, Decline, retry: true);
        Assert.Equal(2, prompts);
        current = current with { Revision = "bad2" };
        await session.LoadAsync(Load, Repair, Decline);
        Assert.Equal(3, prompts);
    }

    [Theory]
    [InlineData("conflict")]
    [InlineData("io")]
    [InlineData("access")]
    public async Task FailedRepairReturnsTheValidVersionSavedByAnotherWindow(string failure)
    {
        var session = new LibraryRepairSession();
        var current = new LibraryLoadResult("corrupt", "[]", "bad");
        var valid = new LibraryLoadResult("loaded", "[{\"id\":\"other-window\"}]", "good");
        Task<LibraryLoadResult> Repair(string revision)
        {
            Assert.Equal("bad", revision);
            current = valid;
            throw failure switch
            {
                "conflict" => new LibraryConflictException("[]", ""),
                "io" => new IOException(),
                _ => new UnauthorizedAccessException(),
            };
        }
        var result = await session.LoadAsync(() => Task.FromResult(current), Repair, () => Task.FromResult(true));
        Assert.Equal(valid, result);
    }

    [Fact]
    public async Task FailedRepairDoesNotPresentAnotherCorruptRevisionAsLoaded()
    {
        var session = new LibraryRepairSession();
        var result = await session.LoadAsync(
            () => Task.FromResult(new LibraryLoadResult("corrupt", "[]", "bad")),
            _ => throw new IOException(), () => Task.FromResult(true));
        Assert.Equal("unavailable", result.Status);
    }

    [Fact]
    public async Task LibraryRepairedWhileQueuedDoesNotPrompt()
    {
        var valid = new LibraryLoadResult("loaded", "[]", "good");
        var result = await new LibraryRepairSession().LoadAsync(
            () => Task.FromResult(valid),
            _ => throw new InvalidOperationException("Already repaired."),
            () => throw new InvalidOperationException("Already repaired."));
        Assert.Equal(valid, result);
    }
}
