using System.Runtime.InteropServices;
using ExcalidrawDesktop.App.Services;
using WinRT.Interop;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private const int DwmTransitionsForceDisabled = 3;
    private bool isPreparedTearOutWindow;

    private MainWindow PreparePendingTearOutWindow()
    {
        pendingTearOutWindow?.CloseIfEmptyAfterMove();
        pendingTearOutWindow = workspaceCoordinator.CreateTearOutPlaceholder();
        pendingTearOutWindow.Closed += OnPendingTearOutWindowClosed;
        return pendingTearOutWindow;
    }

    internal void PrepareTearOutWindow()
    {
        // WinUI shows and then hides the destination while entering its native
        // move loop, even for a click. Disable the entrance animation so this
        // preparatory show stays behind the source window as WinUI intends.
        // Do not cloak the destination: it must participate in the native move
        // loop for a torn-out window to follow the pointer while dragging.
        // https://github.com/microsoft/microsoft-ui-xaml/issues/10155
        SetTearOutWindowAttribute(DwmTransitionsForceDisabled, true);
        AppWindow.IsShownInSwitchers = false;
        isPreparedTearOutWindow = true;
    }

    internal void RevealTearOutWindow()
    {
        if (!isPreparedTearOutWindow)
        {
            return;
        }

        // The live editor is attached and the window has been activated before
        // revealing it. Keep transitions suppressed through WinUI's ensuing Show
        // call, then restore normal minimize/maximize animations.
        AppWindow.IsShownInSwitchers = true;
        isPreparedTearOutWindow = false;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsClosed)
            {
                try
                {
                    SetTearOutWindowAttribute(DwmTransitionsForceDisabled, false);
                }
                catch (COMException exception)
                {
                    DiagnosticLogService.Error("window.tear_out_animation_restore_failed", exception);
                }
            }
        });
    }

    private void SetTearOutWindowAttribute(int attribute, bool enabled)
    {
        var value = enabled ? 1 : 0; // Win32 BOOL is four bytes.
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(
            WindowNative.GetWindowHandle(this), attribute, ref value, sizeof(int)));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        nint window, int attribute, ref int value, int size);
}
