using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class ExternalFileCheckTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "ExcalidrawCheck-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DistinguishesMissingAndUnknownBaseline()
    {
        Assert.Equal(ExternalFileState.Deleted, await ExternalFileCheck.CheckAsync(path, null, null));
        await File.WriteAllTextAsync(path, "original");
        Assert.Equal(ExternalFileState.UnknownBaseline, await ExternalFileCheck.CheckAsync(path, null, null));
        Assert.Equal(ExternalFileState.UnknownBaseline, await ExternalFileCheck.CheckAsync(path, null, null, compareContent: false));
    }

    [Fact]
    public async Task DetectsReplacementWithSameLengthAndTimestamp()
    {
        await File.WriteAllTextAsync(path, "original");
        var baseline = await ExactFileBaseline.CaptureAsync(path);
        Assert.Equal(ExternalFileState.None, await ExternalFileCheck.CheckAsync(path, baseline.Stamp, baseline.ContentHash));
        var timestamp = File.GetLastWriteTimeUtc(path);
        await File.WriteAllTextAsync(path, "replaced");
        File.SetLastWriteTimeUtc(path, timestamp);
        Assert.Equal(ExternalFileState.Modified, await ExternalFileCheck.CheckAsync(path, baseline.Stamp, baseline.ContentHash));
    }

    [Fact]
    public async Task SharingViolationIsUnavailableAndCanRecover()
    {
        if (!OperatingSystem.IsWindows()) return;
        await File.WriteAllTextAsync(path, "original");
        var baseline = await ExactFileBaseline.CaptureAsync(path);
        using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            Assert.Equal(ExternalFileState.Unavailable, await ExternalFileCheck.CheckAsync(path, baseline.Stamp, baseline.ContentHash));
            Assert.Equal(ExternalFileState.Unavailable, await ExternalFileCheck.CheckAsync(path, baseline.Stamp, baseline.ContentHash, compareContent: false));
        }
        Assert.Equal(ExternalFileState.None, await ExternalFileCheck.CheckAsync(path, baseline.Stamp, baseline.ContentHash));
    }

    public void Dispose() => File.Delete(path);
}
