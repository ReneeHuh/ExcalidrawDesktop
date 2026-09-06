using System.Runtime.InteropServices;
using ExcalidrawDesktop.App.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace ExcalidrawDesktop.App;

public sealed partial class MainWindow
{
    private const int DwmTransitionsForceDisabled = 3;
    private bool isPreparedTearOutWindow;
    private InputNonClientPointerSource? tearOutPointerSource;

    private void InitializeTearOutLifetime()
    {
        tearOutPointerSource = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        tearOutPointerSource.ExitedMoveSize += OnTearOutMoveSizeExited;
    }

    private MainWindow RequestTearOutWindow(TabViewItem? tab)
    {
        // This event also fires on ordinary clicks. It has no cancellation
        // property: WinUI unconditionally uses NewWindowId after it returns.
        // A loading/failed editor (or Settings tab) must still get a valid HWND.
        // Apply transfer eligibility only when an actual tear-out is requested.
        LogAction("tab.tear_out_window_requested", "native", tab is null ? null : FindSession(tab));
        return PreparePendingTearOutWindow();
    }

    private bool TryCompletePendingTearOut(TabViewItem? tab)
    {
        var destination = pendingTearOutWindow;
        var session = tab is null ? null : FindSession(tab);
        LogAction("tab.tear_out_started", "drag", session);
        try
        {
            if (destination is not null && session is not null &&
                workspaceCoordinator.MoveSession(
                    this, session, destination, closeDestinationOnFailure: false))
            {
                destination.Closed -= OnPendingTearOutWindowClosed;
                pendingTearOutWindow = null;
                LogAction("tab.tear_out_completed", "drag", session);
                return true;
            }
        }
        catch (Exception exception)
        {
            DiagnosticLogService.Error("tab.tear_out_failed", exception, new
            {
                sessionId = session?.RecoveryId,
            });
        }

        LogAction("tab.tear_out_rejected", "drag", session);
        if (destination is { IsClosed: false })
        {
            // WinUI still calls Show and queries this AppWindow after our
            // callback. Do not destroy it here. End the native move loop and
            // close the empty destination only after ExitedMoveSize returns.
            PostMessage(WindowNative.GetWindowHandle(destination), 0x001F /* WM_CANCELMODE */, 0, 0);
            PostMessage(WindowNative.GetWindowHandle(this), 0x001F /* WM_CANCELMODE */, 0, 0);
        }
        return false;
    }

    private void OnTearOutMoveSizeExited(
        InputNonClientPointerSource sender, ExitedMoveSizeEventArgs args) =>
        QueueDiscardPendingTearOutWindow();

    private void QueueDiscardPendingTearOutWindow()
    {
        var destination = pendingTearOutWindow;
        if (destination is null) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(pendingTearOutWindow, destination)) return;
            destination.Closed -= OnPendingTearOutWindowClosed;
            pendingTearOutWindow = null;
            destination.CloseIfEmptyAfterMove();
        });
    }

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

    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
