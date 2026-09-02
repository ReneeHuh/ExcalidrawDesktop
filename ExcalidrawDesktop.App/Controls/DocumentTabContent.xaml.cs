using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App.Controls;

public sealed partial class DocumentTabContent : UserControl
{
    private WebView2? editor;

    public DocumentTabContent()
    {
        InitializeComponent();
        editor = (WebView2)EditorContainer.Content;
    }

    public WebView2 Editor => editor ?? throw new InvalidOperationException(
        "The editor is hibernated and must be recreated before use.");

    public WebView2 RecreateEditor(string message)
    {
        CloseEditor();
        editor = new WebView2();
        AutomationProperties.SetAutomationId(editor, "ExcalidrawEditorWebView");
        AutomationProperties.SetName(editor, "Excalidraw editor");
        EditorContainer.Content = editor;
        StartupProgress.IsActive = true;
        StartupStatus.Text = message;
        StartupOverlay.Visibility = Visibility.Visible;
        return editor;
    }

    public void HibernateEditor()
    {
        CloseEditor();
        StartupProgress.IsActive = false;
        StartupStatus.Text = "Sleeping — select this tab to resume";
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

    public void ShowFailure(string message)
    {
        StartupProgress.IsActive = false;
        StartupStatus.Text = message;
        StartupOverlay.Visibility = Visibility.Visible;
    }
}
