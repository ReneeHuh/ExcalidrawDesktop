using Microsoft.Web.WebView2.Core;

namespace ExcalidrawDesktop.App.Sessions;

internal sealed record DocumentSessionTransfer(
    DocumentSession Session,
    CoreWebView2? CoreWebView,
    int SourceIndex);
