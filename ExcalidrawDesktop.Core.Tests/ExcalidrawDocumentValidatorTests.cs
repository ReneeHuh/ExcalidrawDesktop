using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Core.Tests;

public sealed class ExcalidrawDocumentValidatorTests
{
    [Fact]
    public void Validate_AcceptsMinimalDocument()
    {
        ExcalidrawDocumentValidator.Validate(
            """{"type":"excalidraw","version":2,"elements":[],"appState":{},"files":{}}""");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"type\":\"excalidraw\",\"elements\":{}}")]
    [InlineData("{\"type\":\"excalidrawlib\",\"elements\":[]}")]
    public void Validate_RejectsInvalidDocument(string content)
    {
        var exception = Assert.Throws<BridgeProtocolException>(() =>
            ExcalidrawDocumentValidator.Validate(content));

        Assert.Equal("DocumentInvalid", exception.Code);
    }

    [Fact]
    public void ValidateForSave_RejectsContentAboveTheUtf8ByteLimit()
    {
        var exception = Assert.Throws<BridgeProtocolException>(() =>
            ExcalidrawDocumentValidator.ValidateForSave(
                """{"type":"excalidraw","elements":[]}""",
                maxBytes: 8));

        Assert.Equal("DocumentTooLarge", exception.Code);
    }
}
