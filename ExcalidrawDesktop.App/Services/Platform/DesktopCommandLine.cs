using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ExcalidrawDesktop.App.Services.Platform;

internal static class DesktopCommandLine
{
    // Unpackaged AppLifecycle Launch arguments contain the complete command
    // line, including the executable. Use Windows quoting rules before the
    // caller filters drawing paths; treating the entire string as one path
    // silently drops files redirected to an already-running instance.
    internal static IReadOnlyList<string> ParseArguments(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        var pointer = CommandLineToArgvW(commandLine, out var count);
        if (pointer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var arguments = new string[count];
            for (var index = 0; index < count; index++)
                arguments[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size)) ?? "";
            return arguments;
        }
        finally { _ = LocalFree(pointer); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
