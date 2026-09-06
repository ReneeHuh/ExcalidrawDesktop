using System.Text;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class ExactFileBaselineTests
{
    [Fact]
    public async Task CapturesBytesAndHashAsOneStableVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"baseline-{Guid.NewGuid():N}.excalidraw");
        try
        {
            var bytes = Encoding.UTF8.GetBytes("same-size-a😀");
            await File.WriteAllBytesAsync(path, bytes);
            var baseline = await ExactFileBaseline.CaptureAsync(path);

            Assert.Equal(bytes, baseline.Bytes);
            Assert.Equal(RecoveryFileBaseline.ComputeContentHash(bytes), baseline.ContentHash);
            Assert.True(baseline.Matches(baseline.Stamp, bytes));
            Assert.False(baseline.Matches(baseline.Stamp, Encoding.UTF8.GetBytes("same-size-b😀")));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DecodesUtf8AndStripsBomWithoutChangingFingerprint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"baseline-{Guid.NewGuid():N}.excalidraw");
        try
        {
            var bytes = new UTF8Encoding(true).GetPreamble()
                .Concat(Encoding.UTF8.GetBytes("背景😀")).ToArray();
            await File.WriteAllBytesAsync(path, bytes);
            var baseline = await ExactFileBaseline.CaptureAsync(path);

            Assert.Equal("背景😀", baseline.Utf8Content);
            Assert.Equal(RecoveryFileBaseline.ComputeContentHash(bytes), baseline.ContentHash);
        }
        finally { File.Delete(path); }
    }
}
