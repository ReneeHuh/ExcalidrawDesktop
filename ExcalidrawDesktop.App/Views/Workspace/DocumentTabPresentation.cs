using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace ExcalidrawDesktop.App.Views.Workspace;

/// <summary>Connects a live native tab to its session's display projection.</summary>
internal static class DocumentTabPresentation
{
    public static void Bind(TabViewItem tab, DocumentTabViewModel viewModel)
    {
        Bind(TabViewItem.HeaderProperty, nameof(DocumentTabViewModel.HeaderText));
        Bind(ToolTipService.ToolTipProperty, nameof(DocumentTabViewModel.DisplayName));
        Bind(AutomationProperties.NameProperty, nameof(DocumentTabViewModel.AutomationName));
        Bind(AutomationProperties.HelpTextProperty, nameof(DocumentTabViewModel.HelpText));

        void Bind(DependencyProperty property, string name) => tab.SetBinding(property,
            new Binding { Source = viewModel, Path = new PropertyPath(name), Mode = BindingMode.OneWay });
    }
}
