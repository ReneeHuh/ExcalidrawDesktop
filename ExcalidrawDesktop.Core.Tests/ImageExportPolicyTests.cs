using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class ImageExportPolicyTests
{
    [Theory]
    [InlineData("Board.excalidraw", "Board")]
    [InlineData("Untitled", "Untitled")]
    [InlineData("...", "Untitled")]
    public void CreatesSafeSuggestedBaseName(string displayName, string expected)
    {
        Assert.Equal(expected, ImageExportPolicy.CreateSuggestedBaseName(displayName));
    }

    [Fact]
    public void MatchesOnlyTheOwningOriginAndExportIdentifier()
    {
        var sessionId = Guid.Parse("9d32585b-1ac7-4f48-9ef0-8b7a55d49570");
        var exportId = Guid.Parse("720e34f1-a3ea-4af3-93ed-a950b2357c42");
        var origin = DesktopTabOrigin.Create(sessionId);

        Assert.True(ImageExportPolicy.IsMatchingUpload(
            $"{ImageExportPolicy.GetUploadOrigin(origin)}/_desktop/export/{exportId:D}",
            origin,
            exportId));
        Assert.False(ImageExportPolicy.IsMatchingUpload(
            $"{ImageExportPolicy.GetUploadOrigin(origin)}/_desktop/export/{Guid.NewGuid():D}",
            origin,
            exportId));
        Assert.False(ImageExportPolicy.IsMatchingUpload(
            $"{origin.Origin}/_desktop/export/{exportId:D}",
            origin,
            exportId));
        Assert.False(ImageExportPolicy.IsMatchingUpload(
            $"{ImageExportPolicy.GetUploadOrigin(origin)}/_desktop/export/{exportId:D}?redirect=true",
            origin,
            exportId));
        Assert.False(ImageExportPolicy.IsMatchingUpload(
            $"https://other.excalidraw.local/_desktop/export/{exportId:D}",
            origin,
            exportId));
    }
}
