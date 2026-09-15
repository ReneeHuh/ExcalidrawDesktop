namespace ExcalidrawDesktop.Core;

/// <summary>Pure window placement rules. Callers supply physical-pixel bounds.</summary>
public static class WindowPlacement
{
    /// <summary>Shrinks the request to fit the work area, then moves it fully inside.</summary>
    public static WorkspaceWindowBounds ClampToWorkArea(
        WorkspaceWindowBounds requested,
        WorkspaceWindowBounds workArea)
    {
        var areaWidth = Math.Max(0, workArea.Width);
        var areaHeight = Math.Max(0, workArea.Height);
        var width = Math.Clamp(requested.Width, 0, areaWidth);
        var height = Math.Clamp(requested.Height, 0, areaHeight);
        var x = Math.Clamp(requested.X, workArea.X, workArea.X + areaWidth - width);
        var y = Math.Clamp(requested.Y, workArea.Y, workArea.Y + areaHeight - height);
        return new WorkspaceWindowBounds(x, y, width, height);
    }

    /// <summary>
    /// Bounds for the window a dropped tab opens. The window takes the source
    /// window's size and is offset so the pointer rests inside its tab strip.
    /// A maximized source yields a window covering most of the work area
    /// rather than a restored window of the maximized size.
    /// </summary>
    public static WorkspaceWindowBounds ForDroppedTab(
        int pointerX,
        int pointerY,
        int sourceWidth,
        int sourceHeight,
        bool sourceMaximized,
        int pointerInsetX,
        int pointerInsetY,
        WorkspaceWindowBounds workArea)
    {
        var width = sourceMaximized ? workArea.Width * 4 / 5 : sourceWidth;
        var height = sourceMaximized ? workArea.Height * 4 / 5 : sourceHeight;
        return ClampToWorkArea(
            new WorkspaceWindowBounds(pointerX - pointerInsetX, pointerY - pointerInsetY, width, height),
            workArea);
    }
}
