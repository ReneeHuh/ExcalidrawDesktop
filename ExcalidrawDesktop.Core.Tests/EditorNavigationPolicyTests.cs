using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Core.Tests;

public sealed class EditorNavigationPolicyTests
{
    [Fact]
    public void ReloadAndSameOriginReplacementCannotDiscardALiveScene()
    {
        const string entry = "https://tab.excalidraw.local/index.html";
        var policy = new EditorNavigationPolicy(entry);
        Assert.True(policy.TryBeginNavigation(entry));
        Assert.False(policy.TryBeginNavigation(entry));
        Assert.False(policy.TryBeginNavigation("https://tab.excalidraw.local/"));
        Assert.False(policy.TryBeginNavigation("about:blank"));
        Assert.False(policy.TryBeginNavigation(entry + "?reload=true"));
    }

    [Fact]
    public void ExplicitlyRecreatedEditorGetsItsOwnInitialNavigation()
    {
        const string entry = "https://tab.excalidraw.local/index.html?desktopSmoke=1";
        var policy = new EditorNavigationPolicy(entry);
        Assert.False(policy.TryBeginNavigation("https://example.com"));
        Assert.True(policy.TryBeginNavigation(entry));
        Assert.True(new EditorNavigationPolicy(entry).TryBeginNavigation(entry));
    }
}
