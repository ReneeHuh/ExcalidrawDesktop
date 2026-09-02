using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class DesktopFileStampTests
{
    [Fact]
    public void DetectsTimestampAndSizeChanges()
    {
        var original = new DesktopFileStamp(DateTimeOffset.UnixEpoch, 100);

        Assert.False(original.DiffersFrom(new DesktopFileStamp(
            DateTimeOffset.UnixEpoch,
            100)));
        Assert.True(original.DiffersFrom(new DesktopFileStamp(
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            100)));
        Assert.True(original.DiffersFrom(new DesktopFileStamp(
            DateTimeOffset.UnixEpoch,
            101)));
    }
}
