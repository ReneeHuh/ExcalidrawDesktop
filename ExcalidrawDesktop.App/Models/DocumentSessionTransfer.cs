using Microsoft.Web.WebView2.Core;

namespace ExcalidrawDesktop.App.Models;

internal sealed record DocumentSessionTransfer(
    DocumentSession Session,
    CoreWebView2? CoreWebView,
    int SourceIndex);
