using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ExcalidrawDesktop.App.Services;

namespace ExcalidrawDesktop.App.Controls;

public sealed partial class DocumentTabContent : UserControl
{
    private WebView2? editor;

    public event EventHandler? RetryRequested;

    public event EventHandler? CloseRequested;

    public event EventHandler? WebView2HelpRequested;

    public DocumentTabContent()
    {
        InitializeComponent();
        editor = (WebView2)EditorContainer.Content;
    }

    public WebView2 Editor => editor ?? throw new InvalidOperationException(
        "The editor is hibernated and must be recreated before use.");

    internal bool HasEditor => editor is not null;

    internal bool IsFailureVisible =>
        StartupOverlay.Visibility == Visibility.Visible &&
        FailureActions.Visibility == Visibility.Visible;

    internal bool IsWebView2HelpVisible =>
        WebView2HelpButton.Visibility == Visibility.Visible;

    public WebView2 RecreateEditor(string message)
    {
        CloseEditor();
        editor = new WebView2();
        AutomationProperties.SetAutomationId(editor, "ExcalidrawEditorWebView");
        AutomationProperties.SetName(
            editor,
            DesktopResources.Get("EditorAutomationName", "Excalidraw editor"));
        EditorContainer.Content = editor;
        StartupProgress.IsActive = true;
        StartupStatus.Text = message;
        StartupGuidance.Text = string.Empty;
        StartupGuidance.Visibility = Visibility.Collapsed;
        FailureActions.Visibility = Visibility.Collapsed;
        WebView2HelpButton.Visibility = Visibility.Collapsed;
        StartupOverlay.Visibility = Visibility.Visible;
        return editor;
    }

    public void HibernateEditor()
    {
        CloseEditor();
        StartupProgress.IsActive = false;
        StartupStatus.Text = DesktopResources.Get(
            "SleepingStatus",
            "Sleeping — select this tab to resume");
        StartupGuidance.Text = string.Empty;
        StartupGuidance.Visibility = Visibility.Collapsed;
        FailureActions.Visibility = Visibility.Collapsed;
        WebView2HelpButton.Visibility = Visibility.Collapsed;
        StartupOverlay.Visibility = Visibility.Visible;
    }

    public void CloseEditor()
    {
        if (editor is null)
        {
            return;
        }

        EditorContainer.Content = null;
        editor.Close();
        editor = null;
    }

    public void ShowReady()
    {
        StartupProgress.IsActive = false;
        StartupOverlay.Visibility = Visibility.Collapsed;
        Editor.Focus(FocusState.Programmatic);
    }

    public void ShowFailure(
        string message,
        string? guidance = null,
        bool showWebView2Help = false)
    {
        StartupProgress.IsActive = false;
        StartupStatus.Text = message;
        StartupGuidance.Text = guidance ?? DesktopResources.Get(
            "EditorFailureGuidance",
            "Retry the editor or close this tab. Your saved drawing is not changed.");
        StartupGuidance.Visibility = Visibility.Visible;
        FailureActions.Visibility = Visibility.Visible;
        WebView2HelpButton.Visibility = showWebView2Help
            ? Visibility.Visible
            : Visibility.Collapsed;
        StartupOverlay.Visibility = Visibility.Visible;
        RetryEditorButton.Focus(FocusState.Programmatic);
    }

    public void ClearActionHandlers()
    {
        RetryRequested = null;
        CloseRequested = null;
        WebView2HelpRequested = null;
    }

    private void OnRetryClick(object sender, RoutedEventArgs args)
    {
        RetryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseTabClick(object sender, RoutedEventArgs args)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnWebView2HelpClick(object sender, RoutedEventArgs args)
    {
        WebView2HelpRequested?.Invoke(this, EventArgs.Empty);
    }
}
