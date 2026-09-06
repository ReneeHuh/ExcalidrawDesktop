using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class PathReservationRegistryTests
{
    [Fact]
    public void DuplicateLeasesAreDeniedAndOldLeaseCannotReleaseNewLease()
    {
        var registry = new PathReservationRegistry();
        var firstOwner = new object();
        var secondOwner = new object();
        Assert.True(registry.TryReserve("drawing.excalidraw", firstOwner, out var first));
        Assert.False(registry.TryReserve("drawing.excalidraw", firstOwner, out _));
        Assert.False(registry.TryReserve("drawing.excalidraw", secondOwner, out _));
        first!.Dispose();
        Assert.True(registry.TryReserve("drawing.excalidraw", secondOwner, out var second));
        first.Dispose();
        Assert.True(registry.IsReservedByAnother("drawing.excalidraw", firstOwner));
        second!.Dispose();
        Assert.False(registry.IsReservedByAnother("drawing.excalidraw", firstOwner));
    }
}
