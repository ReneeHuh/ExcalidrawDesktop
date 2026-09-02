using System.Text.Json;
using ExcalidrawDesktop.Core;

namespace ExcalidrawDesktop.Core.Tests;

public sealed class BridgeProtocolTests
{
    private const string RequestId = "720e34f1-a3ea-4af3-93ed-a950b2357c42";

    [Fact]
    public void Parse_AcceptsVersionOneRequestAndClonesPayload()
    {
        var message = BridgeMessageParser.Parse(
            """{"version":1,"kind":"request","requestId":"REQUEST_ID","method":"document.open","payload":{"hasUnsavedChanges":true}}"""
                .Replace("REQUEST_ID", RequestId));

        Assert.Equal("document.open", message.Method);
        Assert.True(message.Payload?.GetProperty("hasUnsavedChanges").GetBoolean());
    }

    [Fact]
    public void Parse_RejectsUnsupportedVersion()
    {
        var exception = Assert.Throws<BridgeProtocolException>(() =>
            BridgeMessageParser.Parse(
                """{"version":2,"kind":"request","requestId":"REQUEST_ID","method":"app.ping"}"""
                    .Replace("REQUEST_ID", RequestId)));

        Assert.Equal("BridgeVersionUnsupported", exception.Code);
    }

    [Fact]
    public void Parse_RejectsNonCanonicalRequestIdentifier()
    {
        var exception = Assert.Throws<BridgeProtocolException>(() =>
            BridgeMessageParser.Parse(
                """{"version":1,"kind":"request","requestId":"not-a-uuid","method":"app.ping"}"""));

        Assert.Equal("BridgeMessageInvalid", exception.Code);
    }

    [Fact]
    public void ErrorResponse_PreservesCorrelationAndStructuredError()
    {
        var request = BridgeMessageParser.Parse(
            """{"version":1,"kind":"request","requestId":"REQUEST_ID","method":"document.open"}"""
                .Replace("REQUEST_ID", RequestId));
        using var response = JsonDocument.Parse(
            BridgeResponseJson.Error(request, "DocumentInvalid", "Invalid document."));

        Assert.Equal(RequestId, response.RootElement.GetProperty("requestId").GetString());
        Assert.Equal("DocumentInvalid", response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Event_CreateProducesAParseableCorrelatedHostEvent()
    {
        var message = BridgeMessageParser.Parse(
            BridgeEventJson.Create("document.saveRequested", new { reason = "close" }));

        Assert.Equal("event", message.Kind);
        Assert.Equal("document.saveRequested", message.Method);
        Assert.Equal("close", message.Payload?.GetProperty("reason").GetString());
    }
}
