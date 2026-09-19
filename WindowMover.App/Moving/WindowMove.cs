using System.Runtime.InteropServices;
using WindowMover.Core;
using static NativeMethods;

// The move itself: put this window on that monitor.
//
// The decisions around it live elsewhere, one file each - whether the window may be moved
// (MovableWindowCheck), where it should land (MoveTargetBounds), how it is hidden while it
// travels (WindowTransparency), and what happens afterwards if the app fights the size it
// was given (MoveSettleWatcher). What is left here is the sequence of Win32 calls that
// actually relocates a window, which differs entirely between a maximized window and every
// other kind.
internal static class WindowMove
{
    // Moves a window onto a specific screen: maximized windows stay maximized, everything
    // else keeps its relative position and proportional size (see MoveTargetBounds).
    public static void ToMonitor(IntPtr hwnd, Screen target)
    {
        if (!MovableWindowCheck.IsMovable(hwnd))
        {
            DebugLog.Write($"Move refused [{DebugLog.DescribeWindowProcess(hwnd)}]: hwnd={hwnd} is no longer safe to move at click time");
            return;
        }

        // Must happen before the IsZoomed check below, not just before MoveTargetBounds.For -
        // see ISSUES.md #3. A window can become maximized (by the user, independent of this
        // app, or via a previous WindowMover move) while a size correction from an earlier
        // non-maximized move is still pending; resolving it here first - before deciding which
        // move path to take - means MoveSettleWatcher's own maximized check sees the window's
        // *current* state and skips forcing stale, small bounds onto it, instead of a stale
        // timer doing that later regardless of which path ran.
        MoveSettleWatcher.FinishPendingWatch(hwnd);

        DebugLog.Write($"Move [{DebugLog.DescribeWindowProcess(hwnd)}]: hwnd={hwnd}, maximized={IsZoomed(hwnd)}, foreground={(GetForegroundWindow() == hwnd)} -> monitor {target.DeviceName} {target.Bounds}");

        if (IsZoomed(hwnd))
        {
            MoveMaximizedWindow(hwnd, target);
            return;
        }

        var (bounds, watchForSelfResize) = MoveTargetBounds.For(hwnd, target);

        // Shown for the bounds the window is being given, before any size correction settles:
        // the point is to say "it went there, on that monitor" the instant the gesture is
        // made, not to trace the final pixel-exact rectangle a moment later.
        MoveIndicator.Show(target, bounds);

        if (!watchForSelfResize)
        {
            SetBounds(hwnd, bounds);
            return;
        }

        // Setting the correct size and then having a self-resizing app fight it a moment
        // later reads as the window visibly getting it right, then wrong, then right again -
        // jarring even though the total time is similar. Instead: hide the window for the
        // brief window where that fight could happen, and only reveal it once
        // MoveSettleWatcher decides the correction is actually done (however many rounds that
        // takes - the watcher is the single place that un-hides it, so it cannot stay hidden
        // past whatever ends the correction, including the hard deadline).
        //
        // Still set the correct size immediately rather than waiting to see if it gets
        // overridden: most apps here never get overridden at all (VLC, snapped windows,
        // anything not per-monitor-DPI-aware) - waiting would leave those stuck wrong
        // forever. The few that do get overridden are caught and corrected reactively
        // afterwards instead.
        TransparencyRestore? hidden = WindowTransparency.TryHideIfEnabled(hwnd);
        SetBounds(hwnd, bounds);
        MoveSettleWatcher.KeepSizeUntilSettled(hwnd, bounds, hidden);
    }

    // Cycles a window to the next monitor in the screen array
    public static void ToNextMonitor(IntPtr hwnd)
    {
        if (!MovableWindowCheck.IsMovable(hwnd)) return;
        if (!GetWindowRect(hwnd, out RECT r)) return;

        // Hand the monitor bounds to the core and let it work out where the window is and
        // where it goes next
        Screen[] screens = Screen.AllScreens;
        var layout = new MonitorLayout(Array.ConvertAll(screens, s => s.Bounds));

        int current = layout.IndexOfMonitorShowing(ToRectangle(r));
        if (!layout.TryGetNextMonitorIndex(current, out int next))
        {
            DebugLog.Write($"Move refused [{DebugLog.DescribeWindowProcess(hwnd)}]: no next monitor (current index {current}, {screens.Length} screen(s))");
            return; // needs at least 2 monitors
        }

        ToMonitor(hwnd, screens[next]);
    }

    // Moving a maximized window is not a matter of handing it new bounds. A maximized window
    // ignores SetWindowPos, and SetWindowPlacement's rcNormalPosition only describes where it
    // will land when it *leaves* the maximized state - so neither one moves it while it stays
    // maximized. Measured against real apps (ISSUES.md #4): SetWindowPlacement alone moved
    // nothing at all, and following it with minimize-then-maximize - a real state transition,
    // which is what this code used to do - moved Notepad and Edge but did nothing whatsoever
    // for VS Code, which reasserts its own idea of where the window belongs as it re-enters
    // the maximized state. Taking the window out of the maximized state onto the target
    // monitor and maximizing it there is the one sequence that worked for every app tested,
    // and WindowMover.LiveTests pins it down against a real VS Code window.
    //
    // The landing rectangle is the target monitor's working area rather than the window's own
    // remembered restored size, so the intermediate frame is already close to what the window
    // ends up as: the move reads as a jump between monitors instead of a shrink, a jump and a
    // grow. It costs the remembered restored size, which the SetWindowPlacement version
    // overwrote with this same rectangle anyway.
    private static void MoveMaximizedWindow(IntPtr hwnd, Screen target)
    {
        Rectangle landing = target.WorkingArea;

        // Hidden for the whole sequence, not just part of it. Restoring, repositioning and
        // re-maximizing are three visible state changes and Windows animates each one, which
        // is what reads as the move juddering rather than happening. The window is put back
        // once it stops moving - the maximize animation runs on after ShowWindow returns, so
        // revealing here would show the tail of exactly what this is hiding.
        TransparencyRestore? hidden = WindowTransparency.TryHideIfEnabled(hwnd);

        ShowWindow(hwnd, SW_RESTORE);
        SetBounds(hwnd, landing, "maximized move");
        ShowWindow(hwnd, SW_MAXIMIZE);
        MoveIndicator.Show(target, landing);

        if (hidden is { } restore) MoveSettleWatcher.RevealWhenSettled(hwnd, restore);
    }

    // The one call that actually relocates a window, and the one place a refusal is reported.
    // A move that silently does nothing looks, from the outside, exactly like a gesture that
    // never fired at all - and the two have completely different causes. So a move Windows
    // refuses says so, with the error code: access denied here means UIPI (the target window
    // belongs to a higher-integrity process than this one) and no retry will help.
    private static void SetBounds(IntPtr hwnd, Rectangle bounds, string? note = null)
    {
        bool applied = SetWindowPos(hwnd, IntPtr.Zero,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);
        if (applied) return;

        string what = note is null ? $"SetWindowPos to {bounds}" : $"SetWindowPos to {bounds} ({note})";
        DebugLog.Write($"Move FAILED [{DebugLog.DescribeWindowProcess(hwnd)}]: {what} returned false, win32 error {Marshal.GetLastWin32Error()}");
    }
}
