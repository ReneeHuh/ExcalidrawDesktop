using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ExcalidrawDesktop.App.Views.Workspace;

internal static class TabStripGeometry
{
    public static int GetDropIndex(TabView tabs, double pointerX)
    {
        var slots = new TabStripSlot[tabs.TabItems.Count];
        for (var index = 0; index < slots.Length; index++)
        {
            // TabItems also contains unrealized containers with zero or stale
            // geometry. Only ContainerFromIndex identifies the current layout.
            if (tabs.ContainerFromIndex(index) is TabViewItem { ActualWidth: > 0 } item)
            {
                slots[index] = new TabStripSlot(
                    item.TransformToVisual(tabs).TransformPoint(new Point()).X,
                    item.ActualWidth);
            }
        }
        return TabStripDropIndex.FromPointer(pointerX, slots);
    }
}
