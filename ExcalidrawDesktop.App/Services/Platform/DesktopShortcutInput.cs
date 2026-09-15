using System.Runtime.InteropServices;
using ExcalidrawDesktop.App.Services.Logging;
using Windows.Graphics;

namespace ExcalidrawDesktop.App.Services.Platform;

/// <summary>Observes key repeat before WinUI reduces a key message to an accelerator.</summary>
internal sealed class DesktopShortcutInput : IDisposable
{
    private readonly HookProc callback;
    private nint hook;
    private uint key;
    private bool repeated;

    private DesktopShortcutInput() => callback = OnMessage;

    public static DesktopShortcutInput? TryStart()
    {
        var input = new DesktopShortcutInput();
        input.hook = SetWindowsHookEx(3 /* WH_GETMESSAGE */, input.callback, 0, GetCurrentThreadId());
        if (input.hook != 0) return input;
        AppLogger.Warning($"[DesktopShortcutInput] Could not observe shortcut repeats (Error={Marshal.GetLastWin32Error()})");
        return null;
    }

    public bool IsRepeated(uint virtualKey) => key == virtualKey && repeated;

    private nint OnMessage(int code, nuint wParam, nint lParam)
    {
        if (code >= 0 && wParam == 1 /* PM_REMOVE */)
        {
            var message = Marshal.PtrToStructure<NativeMessage>(lParam);
            if (message.Message is 0x0100 or 0x0104) // WM_KEYDOWN / WM_SYSKEYDOWN
            {
                key = (uint)message.WParam;
                repeated = ((long)message.LParam & (1L << 30)) != 0;
            }
            else if (message.Message is 0x0101 or 0x0105) { key = 0; repeated = false; }
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
