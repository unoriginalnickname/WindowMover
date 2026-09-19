using System.Runtime.InteropServices;
using System.Text;
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
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr hWnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

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

    // The indicator is how a move announces itself, so it has to appear - and it has to be
    // the kind of window that changes nothing: it must never take the foreground away from
    // whatever the user was using, and it must take itself down again rather than leaving an
    // overlay parked on the desktop.
    [LiveFact]
    public void A_move_shows_an_indicator_that_never_steals_focus_and_cleans_itself_up()
    {
        using var host = new MoveHost();
        using var window = TestWindow.Restored(Source);
        AssertStartsOn(Source, window.Handle);

        IntPtr foregroundBefore = GetForegroundWindow();

        host.MoveToScreen(window.Handle, Target);

        bool appeared = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline && !appeared)
        {
            appeared = IndicatorWindows().Count > 0;
            Assert.True(GetForegroundWindow() == foregroundBefore,
                "The indicator took the foreground - it must never move focus away from what the user was doing");
            Thread.Sleep(20);
        }

        Assert.True(appeared, "No indicator appeared for the move");

        // It fades out on its own; nothing should be left behind a second later.
        var goneBy = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTime.UtcNow < goneBy && IndicatorWindows().Count > 0) Thread.Sleep(50);
        Assert.Empty(IndicatorWindows());
    }

    // The overlay is a tool window, which is exactly what WindowMoveFilter refuses to move -
    // so the app can never be asked to throw its own indicator across the desk.
    [LiveFact]
    public void The_indicator_is_a_window_this_app_would_refuse_to_move()
    {
        using var host = new MoveHost();
        using var window = TestWindow.Restored(Source);

        host.MoveToScreen(window.Handle, Target);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        List<IntPtr> indicators = IndicatorWindows();
        while (DateTime.UtcNow < deadline && indicators.Count == 0)
        {
            Thread.Sleep(20);
            indicators = IndicatorWindows();
        }

        Assert.NotEmpty(indicators);
        foreach (IntPtr indicator in indicators)
        {
            Assert.Equal(IntPtr.Zero, WindowMoveActions.WindowToCapture(indicator));
        }
    }

    // A maximized move hides the window for the whole restore/reposition/maximize sequence,
    // which means something has to put it back. If that ever fails to happen the window is
    // still there, still in the taskbar, and completely invisible - the worst outcome this
    // code can produce, and the one worth a test of its own.
    [LiveFact]
    public void A_maximized_move_leaves_the_window_visible_again()
    {
        using var host = new MoveHost();
        using var window = TestWindow.Maximized(Source);
        AssertStartsOn(Source, window.Handle);

        host.MoveToScreen(window.Handle, Target);

        // Generous, because it must outlast the scheduler's own hard deadline: if the watch
        // never settles, that deadline is what ends it, and the window must come back either way.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline && (GetWindowLong(window.Handle, GWL_EXSTYLE) & WS_EX_LAYERED) != 0)
            Thread.Sleep(100);

        Assert.True((GetWindowLong(window.Handle, GWL_EXSTYLE) & WS_EX_LAYERED) == 0,
            "The window was left layered after a maximized move - it is invisible and nothing else will put it back");
        Assert.True(IsWindowVisible(window.Handle), "The window is no longer visible to Windows after a maximized move");
        AssertLandsOn(Target, window.Handle);
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

    // Hiding is done by setting a window's alpha, which means an app that was already using
    // alpha for its own purposes has to get exactly its own value back - not "opaque", and
    // not left permanently see-through either.
    [DpiBoundaryFact]
    public void A_window_with_its_own_transparency_gets_it_back_after_a_move()
    {
        const byte OwnAlpha = 160;

        using var host = new MoveHost();
        Screen target = LiveTestEnvironment.MonitorAtDifferentDpiThan(Source)!;
        using var window = TestWindow.Translucent(Source, OwnAlpha);

        host.MoveToScreen(window.Handle, target);

        // Poll rather than sleep: the correction ends when it ends, and the alpha is only
        // back once it has.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline && CurrentAlpha(window.Handle) != OwnAlpha) Thread.Sleep(100);

        Assert.Equal(OwnAlpha, CurrentAlpha(window.Handle));
        Assert.True((GetWindowLong(window.Handle, GWL_EXSTYLE) & WS_EX_LAYERED) != 0,
            "The window's own WS_EX_LAYERED was stripped - this app did not add it and must not remove it");
    }

    private static byte CurrentAlpha(IntPtr hwnd) =>
        GetLayeredWindowAttributes(hwnd, out _, out byte alpha, out _) ? alpha : (byte)255;

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

    // The app's overlay windows, found by the class WinForms gives them plus this app's own
    // process - there is no public handle to them, and a test that guessed by title would be
    // fooled by anything else on the desktop wearing the same one.
    private static List<IntPtr> IndicatorWindows()
    {
        var found = new List<IntPtr>();
        uint ourProcess = (uint)Environment.ProcessId;

        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (pid != ourProcess) return true;
            if ((GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) == 0) return true;
            if (!IsWindowVisible(h)) return true;
            found.Add(h);
            return true;
        }, IntPtr.Zero);

        return found;
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
