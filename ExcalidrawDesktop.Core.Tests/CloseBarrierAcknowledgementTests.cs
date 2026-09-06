using System.Text.Json;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Tests;

public sealed class CloseBarrierAcknowledgementTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("{\"barrierId\":123}")]
    [InlineData("{\"barrierId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"isDirty\":false,\"canClose\":\"false\"}")]
    [InlineData("{\"barrierId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"isDirty\":false,\"canClose\":true}")]
    public void RejectsMalformedMessagesWithoutThrowing(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Null(CloseBarrierAcknowledgement.Read(doc.RootElement));
    }

    [Fact]
    public void UnsavedLibraryIsAnAcknowledgedDecisionRatherThanATimeout()
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToElement(new { barrierId = id, isDirty = true, canClose = false });
        var result = CloseBarrierAcknowledgement.Read(payload);
        Assert.NotNull(result);
        Assert.Equal(id, result.BarrierId);
        Assert.True(result.IsDirty);
        Assert.True(result.HasUnsavedLibrary);
    }
}
