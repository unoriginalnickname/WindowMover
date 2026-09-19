using System.Runtime.InteropServices;
using Xunit;

namespace WindowMover.LiveTests;

// The half of the app that unit tests cannot reach: what Windows and real apps actually do
// when a window is moved. Every decision about where a window should land is in
// WindowMover.Core and covered by WindowMover.Tests - these tests are only about whether the
// window truly ends up there.
//
// They move real windows between real monitors and take the foreground while they run, so
// they are disruptive by nature: see the README for running the quiet suite on its own.
public class WindowMoveLiveTests
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    // A maximized window is the case that was broken for months without anything noticing
    // (ISSUES.md #4): a maximized window ignores its new bounds, so moving it takes a real
    // state transition rather than a reposition.
    [LiveFact]
    public void A_maximized_window_moves_to_the_target_monitor()
    {
        using var loop = new MessageLoopThread();
        Screen source = Screen.PrimaryScreen!;
        Screen target = LiveTestEnvironment.OtherMonitorThan(source)!;
        using var window = TestWindow.Maximized(source);

        Assert.True(IsZoomed(window.Handle), "Test window was not maximized, so this never exercised the maximized path");

        loop.Invoke(() => WindowMoveActions.MoveWindowToScreen(window.Handle, target));

        AssertLandsOn(target, window.Handle);
    }

    // The regression test this suite was built for, and the one test here that fails against
    // the code this replaced. VS Code reasserts its own idea of where a maximized window
    // belongs when it leaves and re-enters that state, so the sequence that moved every other
    // window tested left this one exactly where it started - every time, which is how it was
    // reported: "it's bugged, the window just isn't moving".
    //
    // Launched with a throwaway profile, so this is not something stale in the user's
    // settings, and so the test can never touch the editor they are working in.
    [ElectronFact]
    public void A_maximized_vs_code_window_moves_to_the_target_monitor()
    {
        using var loop = new MessageLoopThread();
        Screen source = Screen.PrimaryScreen!;
        Screen target = LiveTestEnvironment.OtherMonitorThan(source)!;
        using var editor = ElectronWindow.TryLaunchMaximized(source) ?? throw new InvalidOperationException("VS Code window never opened maximized");

        Assert.True(MonitorShowing(editor.Handle).Bounds.Equals(source.Bounds), "VS Code did not start on the source monitor");

        loop.Invoke(() => WindowMoveActions.MoveWindowToScreen(editor.Handle, target));

        AssertLandsOn(target, editor.Handle);
    }

    [LiveFact]
    public void A_restored_window_moves_to_the_target_monitor()
    {
        using var loop = new MessageLoopThread();
        Screen source = Screen.PrimaryScreen!;
        Screen target = LiveTestEnvironment.OtherMonitorThan(source)!;
        using var window = TestWindow.Restored(source);

        loop.Invoke(() => WindowMoveActions.MoveWindowToScreen(window.Handle, target));

        AssertLandsOn(target, window.Handle);
    }

    // Cycling is what Mouse5 does, and it has its own way of failing: it reads which monitor
    // the window is on right now, so a move that never took effect sends it to the same
    // "next" monitor forever.
    [LiveFact]
    public void A_window_cycled_to_the_next_monitor_does_not_stay_put()
    {
        using var loop = new MessageLoopThread();
        Screen source = Screen.PrimaryScreen!;
        using var window = TestWindow.Maximized(source);

        loop.Invoke(() => WindowMoveActions.MoveWindowToNextScreen(window.Handle));

        Screen landed = WaitUntil(window.Handle, screen => !screen.Bounds.Equals(source.Bounds));
        Assert.False(landed.Bounds.Equals(source.Bounds),
            $"Window was still on {source.DeviceName} after cycling to the next monitor");
    }

    // A window that lands behind what was already on the target monitor looks exactly like a
    // window that never moved - the reason bringing it to the front is part of the move
    // rather than a nicety.
    [LiveFact]
    public void A_moved_window_ends_up_in_front_of_what_was_already_there()
    {
        using var loop = new MessageLoopThread();
        Screen source = Screen.PrimaryScreen!;
        Screen target = LiveTestEnvironment.OtherMonitorThan(source)!;

        using var mover = TestWindow.Restored(source);
        using var occupant = TestWindow.Maximized(target);   // created last, so it is in front
        Thread.Sleep(300);

        loop.Invoke(() => WindowMoveActions.MoveWindowToScreen(mover.Handle, target));

        IntPtr front = WaitForForeground(mover.Handle, TimeSpan.FromSeconds(6));
        Assert.True(front == mover.Handle,
            $"Moved window {mover.Handle} did not reach the foreground - {front} is in front instead");
    }

    // Windows reports a move as done long before the window has finished arriving - an app
    // can still be resizing itself in response to a DPI change, and the app's own correction
    // may be hiding it meanwhile. Polling to a deadline is the honest way to ask "did it get
    // there", rather than sleeping a guessed interval and asserting into a race.
    private static void AssertLandsOn(Screen target, IntPtr hwnd)
    {
        Screen landed = WaitUntil(hwnd, screen => screen.Bounds.Equals(target.Bounds));
        Assert.True(landed.Bounds.Equals(target.Bounds),
            $"Window ended on {landed.DeviceName} {landed.Bounds}, expected {target.DeviceName} {target.Bounds} (window rect {RectOf(hwnd)})");
    }

    private static Screen WaitUntil(IntPtr hwnd, Func<Screen, bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        Screen current = MonitorShowing(hwnd);
        while (DateTime.UtcNow < deadline && !condition(current))
        {
            Thread.Sleep(150);
            current = MonitorShowing(hwnd);
        }
        return current;
    }

    private static IntPtr WaitForForeground(IntPtr hwnd, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        IntPtr front = GetForegroundWindow();
        while (DateTime.UtcNow < deadline && front != hwnd)
        {
            Thread.Sleep(150);
            front = GetForegroundWindow();
        }
        return front;
    }

    private static Screen MonitorShowing(IntPtr hwnd) => Screen.FromRectangle(RectOf(hwnd));

    private static Rectangle RectOf(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out RECT r)) return Rectangle.Empty;
        return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}
