using System.Runtime.InteropServices;
using Windows.Graphics;

namespace ExcalidrawDesktop.App.Services.Platform;

/// <summary>Converts physical screen coordinates to a native window's client area.</summary>
internal static class DesktopPointer
{
    public static PointInt32? TryGetClientPosition(nint window, PointInt32 screenPoint)
    {
        var point = new NativePoint { X = screenPoint.X, Y = screenPoint.Y };
        return ScreenToClient(window, ref point) ? new PointInt32(point.X, point.Y) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);
}
