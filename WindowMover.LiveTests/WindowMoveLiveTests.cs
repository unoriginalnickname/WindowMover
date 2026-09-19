using System.Runtime.InteropServices;
using Xunit;

namespace WindowMover.LiveTests;

// The half of the app that unit tests cannot reach: what Windows and real apps actually do
// when a window is moved. Every decision about where a window should land is in
// WindowMover.Core and covered by WindowMover.Tests - these tests are only about whether the
// window truly ends up there.
//
// Each test states the situation it needs before it acts on it. A live test that skips that
// is worse than no test: a window that never maximized, or an occupant that never took the
// foreground, turns the assertion into something that passes for the wrong reason - which is
// exactly how the first version of this suite passed against code that was broken.
//
// They move real windows between real monitors and take the foreground while they run, so
// they are disruptive by nature: see the README for running the quiet suite on its own.
public class WindowMoveLiveTests
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_LAYERED = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static Screen Source => Screen.PrimaryScreen!;
    private static Screen Target => LiveTestEnvironment.OtherMonitorThan(Source)!;

    // A maximized window is the case that was broken for as long as the app had existed
    // without anything noticing (ISSUES.md #4): a maximized window ignores new bounds, so
    // moving one takes a real state transition rather than a reposition.
    [LiveFact]
    public void A_maximized_window_moves_to_the_target_monitor()
    {
        using var host = new MoveHost();
        using var window = TestWindow.Maximized(Source);   // throws unless it really is maximized
        AssertStartsOn(Source, window.Handle);

        host.MoveToScreen(window.Handle, Target);

        AssertLandsOn(Target, window.Handle);
    }

    // The regression test this suite was built for, and the only one here that fails against
    // the code it replaced. VS Code reasserts its own idea of where a maximized window
    // belongs as it re-enters that state, so the sequence that moved every other window
    // tested left this one exactly where it started - every time, which is how it was
    // reported: "it's bugged, the window just isn't moving".
    //
    // It has to be VS Code. Notepad, Edge and an ordinary test window all move correctly
    // under the broken code, so any of them standing in here would report success on a bug.
    [VsCodeFact]
    public void A_maximized_vs_code_window_moves_to_the_target_monitor()
    {
        using var host = new MoveHost();
        using var editor = VsCodeWindow.TryLaunchMaximized(Source) ?? throw new InvalidOperationException("VS Code never opened a maximized window to test with");
        AssertStartsOn(Source, editor.Handle);

        host.MoveToScreen(editor.Handle, Target);

        AssertLandsOn(Target, editor.Handle);
    }

    [LiveFact]
    public void A_restored_window_moves_to_the_target_monitor()
    {
        using var host = new MoveHost();
        using var window = TestWindow.Restored(Source);
        AssertStartsOn(Source, window.Handle);

        host.MoveToScreen(window.Handle, Target);

        AssertLandsOn(Target, window.Handle);
    }

    // Cycling is what Mouse5 does, and it has its own way of failing: it reads which monitor
    // the window is on right now, so a move that never takes effect sends the window to the
    // same "next" monitor forever - which is what the debug log showed, seven times in a row,
    // when this was reported.
    [LiveFact]
    public void A_window_cycled_to_the_next_monitor_leaves_the_one_it_was_on()
    {
        using var host = new MoveHost();
        using var window = TestWindow.Maximized(Source);
        AssertStartsOn(Source, window.Handle);

        host.MoveToNextScreen(window.Handle);

        Screen landed = WaitUntil(window.Handle, screen => !IsSameMonitor(screen, Source));
        Assert.False(IsSameMonitor(landed, Source),
            $"Window was still on {Source.DeviceName} after being cycled to the next monitor");
    }

    // A window that lands behind what was already on the target monitor looks exactly like a
    // window that never moved - which is why bringing it to the front is part of the move
    // rather than a nicety.
    [LiveFact]
    public void A_moved_window_ends_up_in_front_of_what_was_already_there()
    {
        using var host = new MoveHost();
        using var mover = TestWindow.Restored(Source);
        using var occupant = TestWindow.Maximized(Target);

        // Showing a window from a background process does not make it the foreground window -
        // the foreground lock refuses that. So the occupant is put in front the same way the
        // app puts a moved window in front, and the test insists it worked: without a window
        // genuinely in front, the assertion below could be satisfied by a move that did
        // nothing about Z-order at all.
        host.BringToFront(occupant.Handle);
        Assert.True(WaitForForeground(occupant.Handle, TimeSpan.FromSeconds(3)) == occupant.Handle,
            "The occupying window never took the foreground, so there was nothing to end up in front of");

        host.MoveToScreen(mover.Handle, Target);

        IntPtr front = WaitForForeground(mover.Handle, TimeSpan.FromSeconds(6));
        Assert.True(front == mover.Handle,
            $"Moved window {mover.Handle} never reached the foreground - {front} is in front of it");
    }

    // Moving across a DPI boundary puts the window through the correction pass, during which
    // the app hides it. It must do that without the window ever stopping being visible as far
    // as Windows is concerned: the shell drops a window's taskbar button the moment it does,
    // and hands it back afterwards wherever it likes. That is what ShowWindow(SW_HIDE) did
    // here, and it is why the hide is a transparency now.
    [DpiBoundaryFact]
    public void A_window_hidden_during_correction_never_leaves_the_taskbar()
    {
        using var host = new MoveHost();
        Screen target = LiveTestEnvironment.MonitorAtDifferentDpiThan(Source)!;
        using var window = TestWindow.Restored(Source);
        AssertStartsOn(Source, window.Handle);

        host.MoveToScreen(window.Handle, target);

        // Watch across the whole correction: it ends on its own, but never later than the
        // scheduler's hard deadline.
        bool everHidden = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            Assert.True(IsWindowVisible(window.Handle),
                "Window stopped being visible to Windows during the correction - the shell drops its taskbar button when that happens");
            everHidden |= (GetWindowLong(window.Handle, GWL_EXSTYLE) & WS_EX_LAYERED) != 0;
            Thread.Sleep(25);
        }

        Assert.True(everHidden, "The window was never actually hidden, so this proved nothing about how it is hidden");
        Assert.True((GetWindowLong(window.Handle, GWL_EXSTYLE) & WS_EX_LAYERED) == 0,
            "Window was left layered after the correction finished - this app added that style and has to take it back off");
    }

    private static void AssertStartsOn(Screen expected, IntPtr hwnd)
    {
        Screen actual = MonitorShowing(hwnd);
        Assert.True(IsSameMonitor(actual, expected),
            $"Window started on {actual.DeviceName}, not {expected.DeviceName} - this test never moved it between monitors");
    }

    // Windows reports a move as done long before the window has finished arriving - an app
    // can still be resizing itself in response to a DPI change, and the app's own correction
    // may be hiding it meanwhile. Polling to a deadline is the honest way to ask "did it get
    // there", rather than sleeping a guessed interval and asserting into a race.
    private static void AssertLandsOn(Screen target, IntPtr hwnd)
    {
        Screen landed = WaitUntil(hwnd, screen => IsSameMonitor(screen, target));
        Assert.True(IsSameMonitor(landed, target),
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

    // Screen objects are rebuilt on every call, so two Screens for one monitor are never the
    // same instance - DeviceName is the identity that survives that.
    private static bool IsSameMonitor(Screen a, Screen b) => a.DeviceName == b.DeviceName;

    private static Screen MonitorShowing(IntPtr hwnd) => Screen.FromRectangle(RectOf(hwnd));

    private static Rectangle RectOf(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out RECT r)) throw new InvalidOperationException($"Window {hwnd} is gone - GetWindowRect failed");
        return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}
