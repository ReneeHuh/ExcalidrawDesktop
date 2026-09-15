using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ExcalidrawDesktop.App;

// Equal tab widths that shrink together as tabs are added. TabWidthMode.Equal
// and the TabViewItemMinWidth/MaxWidth resources in App.xaml describe the same
// policy, but WinUI can keep content-sized headers across insertions and
// removals, so each item is constrained here as well.
public sealed partial class MainWindow
{
    /// <summary>Width of the add-tab button and its margins in the strip footer.</summary>
    private const double AddTabButtonWidth = 48;
    private static readonly double TabMinWidth = ReadTabWidthResource("TabViewItemMinWidth", 100);
    private static readonly double TabMaxWidth = ReadTabWidthResource("TabViewItemMaxWidth", 220);

    private static double ReadTabWidthResource(string key, double fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is double width
            ? width
            : fallback;

    private void OnTabStripLoaded(object sender, RoutedEventArgs args) => UpdateTabWidths();

    private void OnTabStripSizeChanged(object sender, SizeChangedEventArgs args) => UpdateTabWidths();

    private void UpdateTabWidths()
    {
        var count = DocumentTabs.TabItems.Count;
        if (count == 0 || DocumentTabs.ActualWidth <= 0)
        {
            return;
        }

        var headerWidth = (DocumentTabs.TabStripHeader as FrameworkElement)?.ActualWidth ?? 0;
        // Reserve the caption drag area and the add button.
        var availableWidth = DocumentTabs.ActualWidth - headerWidth - WindowDragRegion.MinWidth - AddTabButtonWidth;
        var width = Math.Clamp(availableWidth / count, TabMinWidth, TabMaxWidth);
        var scale = DocumentTabs.XamlRoot?.RasterizationScale ?? 1;
        width = Math.Floor(width * scale) / scale;
        foreach (var tab in DocumentTabs.TabItems.OfType<TabViewItem>())
        {
            tab.MinWidth = width;
            tab.MaxWidth = width;
        }
    }
}
