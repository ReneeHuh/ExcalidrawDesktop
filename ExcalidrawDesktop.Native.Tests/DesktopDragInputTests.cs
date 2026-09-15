using System.Runtime.InteropServices;
using ExcalidrawDesktop.App.Services.Platform;
using Windows.Graphics;
using Xunit;

namespace ExcalidrawDesktop.Native.Tests;

public sealed class DesktopDragInputTests
{
    [Theory]
    [InlineData(0x0100, 0x1B)] // Escape
    [InlineData(0x0104, 0x1B)] // Alt+Escape
    [InlineData(0x0204, 0)] // Right click
    [InlineData(0x00A4, 0)] // Nonclient right click
    public void CancellationCannotBecomeARelease(uint message, uint key)
    {
        var state = new DesktopDragInputState();
        state.Observe(message, key, new PointInt32(10, 20));
        state.Observe(0x0202, 0, new PointInt32(30, 40));
        Assert.Null(state.ReleasePoint);
    }

    [Fact]
    public void ReleaseCapturesItsPositionBeforeLaterInput()
    {
        var state = new DesktopDragInputState();
        state.Observe(0x0202, 0, new PointInt32(-200, 300));
        state.Observe(0x0100, 0x1B, new PointInt32(10, 20));
        state.Observe(0x0202, 0, new PointInt32(30, 40));
        Assert.Equal(new PointInt32(-200, 300), state.ReleasePoint);
    }

    [Theory]
    [InlineData(0x20000001, true)] // Primary pointer
    [InlineData(0xA0000001, false)] // Canceled primary pointer
    [InlineData(0x00000002, false)] // Secondary pointer
    public void PointerReleaseRequiresAnUncanceledPrimaryPointer(uint key, bool released)
    {
        var state = new DesktopDragInputState();
        state.Observe(0x0247, key, new PointInt32(10, 20));
        Assert.Equal(released, state.ReleasePoint is not null);
    }

    [Fact]
    public void ObservesDequeuedMessagesAndStopsWhenDisposed()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Create this test thread's queue. Posted messages never reach
                // another application's window or synthesize physical input.
                PeekMessage(out _, 0, 0, 0, 0);
                using var canceled = DesktopDragInput.TryStart();
                Assert.NotNull(canceled);
                PostAndRead(0x0100, 0x1B);
                PostAndRead(0x0202, 0);
                Assert.Null(canceled.State.ReleasePoint);
                canceled.Dispose();

                using var disposed = DesktopDragInput.TryStart();
                Assert.NotNull(disposed);
                disposed.Dispose();
                PostAndRead(0x0202, 0);
                Assert.Null(disposed.State.ReleasePoint);

                using var released = DesktopDragInput.TryStart();
                Assert.NotNull(released);
                PostAndRead(0x0202, 0);
                Assert.NotNull(released.State.ReleasePoint);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void PostAndRead(uint message, nuint key)
    {
        Assert.True(PostThreadMessage(GetCurrentThreadId(), message, key, 0));
        Assert.True(PeekMessage(out _, 0, message, message, 1));
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

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, nint window, uint min, uint max, uint remove);
}
