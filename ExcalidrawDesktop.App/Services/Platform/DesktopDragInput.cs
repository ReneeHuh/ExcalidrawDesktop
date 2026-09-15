using System.Runtime.InteropServices;
using ExcalidrawDesktop.App.Services.Logging;
using Windows.Graphics;

namespace ExcalidrawDesktop.App.Services.Platform;

/// <summary>
/// Observes the current UI thread's drag loop, which consumes cancellation keys
/// before XAML receives them. Installed only for the lifetime of one tab drag.
/// </summary>
internal sealed class DesktopDragInput : IDisposable
{
    private readonly HookProc callback;
    private nint hook;
    internal DesktopDragInputState State { get; } = new();

    private DesktopDragInput() => callback = OnMessage;

    public static DesktopDragInput? TryStart()
    {
        var input = new DesktopDragInput();
        input.hook = SetWindowsHookEx(3 /* WH_GETMESSAGE */, input.callback, 0, GetCurrentThreadId());
        if (input.hook != 0) return input;
        AppLogger.Warning($"[DesktopDragInput] Could not observe tab drag input (Error={Marshal.GetLastWin32Error()})");
        return null;
    }

    private nint OnMessage(int code, nuint wParam, nint lParam)
    {
        if (code >= 0 && wParam == 1 /* PM_REMOVE */)
        {
            var message = Marshal.PtrToStructure<NativeMessage>(lParam);
            var point = message.Message == 0x0247 /* WM_POINTERUP */
                ? new PointInt32(unchecked((short)(long)message.LParam), unchecked((short)((long)message.LParam >> 16)))
                : message.Point;
            State.Observe(message.Message, message.WParam, point);
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (hook == 0) return;
        UnhookWindowsHookEx(hook);
        hook = 0;
        GC.KeepAlive(callback);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public PointInt32 Point;
        public uint Private;
    }

    private delegate nint HookProc(int code, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();
}

internal sealed class DesktopDragInputState
{
    private bool ended;
    public PointInt32? ReleasePoint { get; private set; }

    public void Observe(uint message, nuint key, PointInt32 point)
    {
        if (ended) return;
        if ((message is 0x0100 or 0x0104 && key == 0x1B) || // Escape
            message is 0x0204 or 0x00A4) // Right button down (client/nonclient)
        {
            ended = true;
        }
        else if (message is 0x0202 or 0x00A2) // Left button released
        {
            ended = true;
            ReleasePoint = point;
        }
        else if (message == 0x0247 && ((key >> 16) & 0x2000) != 0) // Primary WM_POINTERUP
        {
            ended = true;
            if (((key >> 16) & 0x8000) == 0) ReleasePoint = point; // Not POINTER_FLAG_CANCELED
        }
    }
}
