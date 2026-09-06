using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class RecoveryFileBaselineTests
{
    private const string Drawing = """{"type":"excalidraw","elements":[],"appState":{"viewBackgroundColor":"#ff0000"},"files":{}}""";
    private const string Path = @"C:\Drawings\drawing.excalidraw";
    private static readonly DesktopFileStamp Stamp = new(DateTimeOffset.Parse("2026-01-02T03:04:05Z"), 1234);

    [Fact]
    public void PairsTheOriginalFileVersionWithAValidDrawing()
    {
        var snapshot = RecoveryFileBaseline.Attach(Drawing, new(Path, Stamp));
        ExcalidrawDocumentValidator.Validate(snapshot);
        Assert.Contains("#ff0000", snapshot);
        Assert.Equal(Stamp, RecoveryFileBaseline.ReadStamp(snapshot, Path));
        Assert.Null(RecoveryFileBaseline.ReadStamp(snapshot, @"C:\Drawings\other.excalidraw"));
    }

    [Fact]
    public void UnknownBaselinesDoNotInheritMetadataFromAnImportedDrawing()
    {
        var snapshot = RecoveryFileBaseline.Attach(Drawing, new(Path, Stamp));
        snapshot = RecoveryFileBaseline.Attach(snapshot, null);
        Assert.Null(RecoveryFileBaseline.ReadStamp(snapshot, Path));
    }

    [Fact]
    public void ContentFingerprintDetectsSameSizeReplacement()
    {
        var baseline = new RecoveryFileBaseline(
            Path,
            Stamp,
            RecoveryFileBaseline.ComputeContentHash(Drawing));

        Assert.True(baseline.MatchesContent(Drawing));
        Assert.False(baseline.MatchesContent(Drawing.Replace("ff0000", "00ff00")));
    }

    [Fact]
    public void RecoveryMetadataPreservesNonAsciiWithoutEscapingThePayload()
    {
        var drawing = Drawing.Replace("#ff0000", "背景色-😀");
        var snapshot = RecoveryFileBaseline.Attach(drawing, new(Path, Stamp));

        using var parsed = System.Text.Json.JsonDocument.Parse(snapshot);
        Assert.Equal(
            "背景色-😀",
            parsed.RootElement.GetProperty("appState").GetProperty("viewBackgroundColor").GetString());
    }

    [Fact]
    public void DocumentContentExcludesNativeEnvelope()
    {
        var snapshot = RecoveryFileBaseline.Attach(Drawing, new(Path, Stamp));
        var document = RecoveryFileBaseline.GetDocumentContent(snapshot);

        Assert.DoesNotContain("desktopRecovery", document);
        ExcalidrawDocumentValidator.Validate(document);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"desktopRecovery\":null}")]
    [InlineData("{\"desktopRecovery\":{}}")]
    [InlineData("{\"desktopRecovery\":{\"stamp\":\"invalid\"}}")]
    public void OldOrDamagedMetadataCannotAuthorizeOverwriting(string snapshot)
    {
        Assert.Null(RecoveryFileBaseline.ReadStamp(snapshot, Path));
    }
}
