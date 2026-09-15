using System.Runtime.InteropServices;
using Windows.Graphics;

namespace ExcalidrawDesktop.App.Services.Platform;

/// <summary>Screen-space pointer queries for input events that carry no position.</summary>
internal static class DesktopPointer
{
    /// <summary>The cursor position in physical screen pixels, or null when unavailable.</summary>
    public static PointInt32? TryGetScreenPosition() =>
        GetCursorPos(out var point) ? new PointInt32(point.X, point.Y) : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
