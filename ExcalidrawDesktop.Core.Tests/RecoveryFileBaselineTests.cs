using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class RecoveryFileBaselineTests
{
    [Fact]
    public void OrphanMetadataRetainsItsValidatedFileAssociation()
    {
        var path = System.IO.Path.GetFullPath("orphan.excalidraw");
        var baseline = new RecoveryFileBaseline(path, new DesktopFileStamp(DateTimeOffset.UtcNow, 3), "hash");
        var snapshot = RecoveryFileBaseline.Attach("{}", baseline);
        Assert.Equal(baseline, RecoveryFileBaseline.ReadEmbedded(snapshot));
        Assert.Null(RecoveryFileBaseline.ReadEmbedded("{\"desktopRecovery\":{\"path\":123}}"));
        Assert.Null(RecoveryFileBaseline.ReadEmbedded("{\"desktopRecovery\":{\"path\":\"bad\\u0000path\"}}"));
    }
    private const string Drawing = """{"type":"excalidraw","elements":[],"appState":{"viewBackgroundColor":"#ff0000"},"files":{}}""";
    private const string Path = @"C:\Drawings\drawing.excalidraw";
    private static readonly DesktopFileStamp Stamp = new(DateTimeOffset.Parse("2026-01-02T03:04:05Z"), 1234);

    [Fact]
    public void RewritingRemovesEveryIncomingMetadataPropertyAndPreservesRawValues()
    {
        const string content = """{"number":1.2300e+02,"text":"背景色-😀","nested":{"desktopRecovery":1},"desktopRecovery":{"path":"wrong"},"desktop\u0052ecovery":42}""";
        var result = RecoveryFileBaseline.Attach(content, new(Path, Stamp));
        using var document = System.Text.Json.JsonDocument.Parse(result);
        Assert.Single(document.RootElement.EnumerateObject(), property => property.Name == "desktopRecovery");
        Assert.Equal(Stamp, RecoveryFileBaseline.ReadStamp(result, Path));
        Assert.Contains("1.2300e+02", result);
        Assert.Contains("背景色-😀", result);
        Assert.Equal(1, document.RootElement.GetProperty("nested").GetProperty("desktopRecovery").GetInt32());
        using var stripped = System.Text.Json.JsonDocument.Parse(RecoveryFileBaseline.GetDocumentContent(result));
        Assert.False(stripped.RootElement.TryGetProperty("desktopRecovery", out _));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"elements\":[}")]
    [InlineData("{} {}")]
    [InlineData("{\"elements\":[1]")]
    public void RewritingRejectsMalformedDocuments(string content)
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => RecoveryFileBaseline.Attach(content, null));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => RecoveryFileBaseline.GetDocumentContent(content));
    }

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
