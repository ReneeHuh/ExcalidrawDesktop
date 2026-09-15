using System.Text.Json;
using ExcalidrawDesktop.App.Sessions;
using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
#if DEBUG
    private static async Task WaitForEditorThemeScriptAsync(DocumentSession session, string script)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        do
        {
            if (await session.CoreWebView!.ExecuteScriptAsync(
                    "(() => { const ownerDocument = document.getElementById('root').ownerDocument; " +
                    "const ownerWindow = ownerDocument.defaultView; return " + script + "; })()") == "true") return;
            await Task.Delay(50);
        } while (DateTimeOffset.UtcNow < deadline);
        throw new InvalidOperationException($"Editor theme check timed out: {script}");
    }

    private static Task AssertEditorThemeForSmokeAsync(DocumentSession session, bool dark) =>
        WaitForEditorThemeScriptAsync(session,
            $"ownerDocument.documentElement.dataset.theme === '{(dark ? "dark" : "light")}' && " +
            $"ownerDocument.querySelector('.excalidraw')?.classList.contains('theme--dark') === {dark.ToString().ToLowerInvariant()}");

    private async Task RunEditorStartupThemeSmokeAsync(DocumentSession session)
    {
        DocumentTabs.SelectedItem = session.TabItem;
        MainLayout.RequestedTheme = ElementTheme.Light;
        await AssertEditorThemeForSmokeAsync(session, dark: false);
        var core = session.CoreWebView!;
        var loadId = Guid.NewGuid();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            using var json = JsonDocument.Parse(args.WebMessageAsJson);
            var message = json.RootElement;
            if (message.GetProperty("method").GetString() == "document.loadApplied" &&
                message.GetProperty("payload").GetProperty("loadId").GetGuid() == loadId)
                applied.TrySetResult();
        }
        core.WebMessageReceived += OnMessage;
        try
        {
            // Hold one real file read after loadFromBlob captures the light
            // editor state. This makes the startup theme/load race deterministic.
            await core.ExecuteScriptAsync("""
                (() => {
                  const ownerWindow = document.getElementById('root').ownerDocument.defaultView;
                  const original = ownerWindow.FileReader.prototype.readAsText;
                  ownerWindow.__themeSmokeRestore = () => { ownerWindow.FileReader.prototype.readAsText = original; };
                  ownerWindow.FileReader.prototype.readAsText = function (...args) {
                    ownerWindow.__themeSmokeRestore();
                    ownerWindow.__themeSmokeRelease = () => original.apply(this, args);
                  };
                })()
                """);
            session.TryPostEditorMessage(BridgeEventJson.Create("document.loadRequested", new
            {
                loadId,
                fileName = "startup-theme.excalidraw",
                content = """{"type":"excalidraw","version":2,"elements":[],"appState":{"viewBackgroundColor":"#ffffff"},"files":{}}""",
                isRecovery = false,
            }));
            await WaitForEditorThemeScriptAsync(session, "typeof ownerWindow.__themeSmokeRelease === 'function'");
            MainLayout.RequestedTheme = ElementTheme.Dark;
            await AssertEditorThemeForSmokeAsync(session, dark: true);
            await core.ExecuteScriptAsync("document.getElementById('root').ownerDocument.defaultView.__themeSmokeRelease()");
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100); // Let the loaded scene's React render commit.
            await AssertEditorThemeForSmokeAsync(session, dark: true);
        }
        finally
        {
            core.WebMessageReceived -= OnMessage;
            await core.ExecuteScriptAsync("(() => { const ownerWindow = document.getElementById('root').ownerDocument.defaultView; " +
                "ownerWindow.__themeSmokeRestore?.(); delete ownerWindow.__themeSmokeRestore; delete ownerWindow.__themeSmokeRelease; })()");
        }
    }
#endif
}
