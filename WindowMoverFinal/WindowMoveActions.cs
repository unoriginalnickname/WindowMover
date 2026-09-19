using System.Runtime.InteropServices;
using WindowMover.Core;
using static NativeMethods;

// The window-movement half: deciding whether a window may be moved, and moving it onto a
// target monitor (maximized or not, with DPI self-correction handled by
// DpiCorrectionScheduler). The decisions about *which* monitor and *what size* live in
// WindowMover.Core, which knows nothing about Win32 and can therefore be tested.
internal static class WindowMoveActions
{
    // User-facing toggle (tray menu) for the hide-during-correction behavior below. On by
    // default for the cleaner visual result; off trades that back for a visible flash in
    // exchange for never touching the window's visibility state, which some apps (confirmed:
    // Chrome/YouTube's spacebar-to-pause) treat as a real backgrounding signal.
    public static bool HideDuringDpiCorrection { get; set; } = true;

    // Fallback size for the rare case a window's current bounds or monitor can't be read,
    // leaving nothing to size proportionally from - see DetermineWindowBounds. The maximized
    // path no longer has any use for it: it lands on the target monitor's working area.
    private const int WindowWidth = 800;
    private const int WindowHeight = 600;

    // How long to wait after a move before forcing the window to the front. This can't be
    // done inline: the middle click that triggers the move has NOT been delivered yet when
    // the hook runs - it goes out to whatever sits under the cursor on the target monitor
    // straight afterwards, and clicking a background window activates it, dropping the
    // window we just moved right back behind it. Waiting a beat on the message loop lets
    // that click land first, so ours is the last word on the Z-order.
    private const int BringToFrontDelayMs = 60;

    // Progman and Shell_TrayWnd are singleton top-level windows that exist for the lifetime
    // of the Explorer shell - looked up once and cached rather than on every move gesture,
    // since IsSafeMovableWindow runs twice per move (capture, then move) and every uncached
    // FindWindow call works against this app's own lightweight/non-invasive goal. Re-looked-up
    // whenever the cached handle is no longer a live window (e.g. Explorer restarting) - a
    // destroyed HWND does not become zero on its own, so a zero-only check would never catch this.
    private static IntPtr cachedProgmanWindow;
    private static IntPtr cachedShellTrayWindow;

    private static IntPtr ProgmanWindow()
    {
        if (!IsWindow(cachedProgmanWindow)) cachedProgmanWindow = FindWindow("Progman", null);
        return cachedProgmanWindow;
    }

    private static IntPtr ShellTrayWindow()
    {
        if (!IsWindow(cachedShellTrayWindow)) cachedShellTrayWindow = FindWindow("Shell_TrayWnd", null);
        return cachedShellTrayWindow;
    }

    // The window to hand to the combo tracker: the given one if we're allowed to move it,
    // otherwise nothing. A refusal here is invisible to the user - the gesture simply does
    // nothing later - so it gets logged: "nothing happened at all" needs to be as traceable
    // as a move that happened and went wrong.
    public static IntPtr WindowToCapture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            DebugLog.Write("Capture: nothing captured - no foreground window");
            return IntPtr.Zero;
        }

        WindowSnapshot snapshot = Describe(hwnd);
        if (WindowMoveFilter.IsSafeToMove(snapshot)) return hwnd;

        DebugLog.Write($"Capture refused [{DebugLog.DescribeWindowProcess(hwnd)}]: hwnd={hwnd}, {snapshot}");
        return IntPtr.Zero;
    }

    // Reads out everything the filter needs to know about a window
    private static WindowSnapshot Describe(IntPtr hwnd)
    {
        return new WindowSnapshot(
            IsTaskbar: hwnd == ShellTrayWindow(),
            IsVisible: IsWindowVisible(hwnd),
            ExtendedStyle: GetWindowLong(hwnd, GWL_EXSTYLE),
            Bounds: GetWindowRect(hwnd, out RECT r) ? ToRectangle(r) : null,
            // The desktop's icons and wallpaper are children of the Progman window
            IsDesktopChild: GetParent(hwnd) == ProgmanWindow());
    }

    // Checks if a window is safe to move (filters out system windows)
    private static bool IsSafeMovableWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        return WindowMoveFilter.IsSafeToMove(Describe(hwnd));
    }

    // Moves a window onto a specific screen: maximized windows stay maximized, everything
    // else keeps its relative position and proportional size (see DetermineWindowBounds).
    public static void MoveWindowToScreen(IntPtr hwnd, Screen target)
    {
        if (!IsSafeMovableWindow(hwnd))
        {
            DebugLog.Write($"Move refused [{DebugLog.DescribeWindowProcess(hwnd)}]: hwnd={hwnd} is no longer safe to move at click time");
            return;
        }

        // Must happen before the IsZoomed check below, not just before DetermineWindowBounds -
        // see ISSUES.md #3. A window can become maximized (by the user, independent of this
        // app, or via a previous WindowMover move) while a DPI correction from an earlier
        // non-maximized move is still pending; resolving it here first - before deciding which
        // move path to take - means ResolvePendingDpiCorrection's own maximized check (below)
        // sees the window's *current* state and skips forcing stale, small bounds onto it,
        // instead of a stale timer doing that later regardless of which path ran.
        DpiCorrectionScheduler.ResolvePendingDpiCorrection(hwnd);

        DebugLog.Write($"Move [{DebugLog.DescribeWindowProcess(hwnd)}]: hwnd={hwnd}, maximized={IsZoomed(hwnd)}, foreground={(GetForegroundWindow() == hwnd)} -> monitor {target.DeviceName} {target.Bounds}");

        if (IsZoomed(hwnd))
        {
            MoveMaximizedWindowToScreen(hwnd, target);
            return;
        }

        var (bounds, watchForSelfResize) = DetermineWindowBounds(hwnd, target);

        if (watchForSelfResize)
        {
            // Setting the correct size and then having a self-resizing app fight it a moment
            // later reads as the window visibly getting it right, then wrong, then right again -
            // jarring even though the total time is similar. Instead: hide the window for the
            // brief window where that fight could happen, and only reveal it once
            // DpiCorrectionScheduler decides the correction is actually done (however many
            // rounds that takes - see RemovePending, the single place that un-hides it, so it
            // can't stay hidden past whatever ends the correction, including the hard deadline).
            //
            // Hidden by making the window fully transparent, NOT by ShowWindow(SW_HIDE),
            // which this used to do. SW_HIDE stops the window being visible as far as Windows
            // is concerned, and the shell drops its taskbar button the moment that happens -
            // so every corrected move flashed the taskbar and could hand the button back in a
            // different position. Transparency leaves IsWindowVisible true throughout
            // (measured), so the shell never notices anything, and it should also avoid the
            // backgrounding signal SW_HIDE sent to Chrome - confirmed live back then to
            // interrupt YouTube's spacebar-to-pause right after a move. HideDuringDpiCorrection
            // remains the tray-menu escape hatch: default on for the cleaner visual result,
            // off to leave the window untouched and accept the flash.
            // Still set the correct size immediately rather than waiting to see if it gets
            // overridden: most apps here never get overridden at all (VLC, snapped windows,
            // anything not per-monitor-DPI-aware) - waiting would leave those stuck wrong
            // forever. The few that do get overridden are caught and corrected reactively
            // by DpiCorrectionScheduler below instead.
            bool hidden = HideDuringDpiCorrection && TryHideByTransparency(hwnd);
            ReportMoveOutcome(hwnd, SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_NOACTIVATE), $"SetWindowPos to {bounds}");
            // While it is transparent there is nothing to see, so raising it would be raising
            // an invisible window - the scheduler does the bring-to-front when it reveals it,
            // from the single place that undoes the transparency.
            DpiCorrectionScheduler.ScheduleDpiCompensationCheck(hwnd, bounds, hiddenForCorrection: hidden);
            if (!hidden) BringToFrontAfterClickLands(hwnd);
        }
        else
        {
            ReportMoveOutcome(hwnd, SetWindowPos(hwnd, IntPtr.Zero,
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                SWP_NOZORDER | SWP_NOACTIVATE), $"SetWindowPos to {bounds}");
            BringToFrontAfterClickLands(hwnd);
        }
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
    private static void MoveMaximizedWindowToScreen(IntPtr hwnd, Screen target)
    {
        Rectangle landing = target.WorkingArea;

        ShowWindow(hwnd, SW_RESTORE);
        ReportMoveOutcome(hwnd, SetWindowPos(hwnd, IntPtr.Zero,
            landing.X, landing.Y, landing.Width, landing.Height,
            SWP_NOZORDER | SWP_NOACTIVATE), $"SetWindowPos to {landing} (maximized move)");
        ShowWindow(hwnd, SW_MAXIMIZE);
        BringToFrontAfterClickLands(hwnd);
    }

    // Where and what size a non-maximized window should become: it lands at the same
    // relative position it had on its source monitor, at the same percentage of screen size
    // it had before, so it neither jumps to an unrelated spot nor looks tiny/oversized
    // crossing between differently sized monitors - see WindowPlacement.ProportionalPosition
    // and ProportionalSize. Falls back to a fixed size centered on the target monitor when
    // the window's current bounds or monitor can't be determined, same as the app has
    // always done.
    //
    // Also returns WatchForSelfResize: true only when this move crosses a real DPI boundary on
    // a per-monitor-DPI-aware window - the only case where the target app might change this
    // size on its own afterward (Chrome/Explorer auto-resize themselves the moment they detect
    // a DPI change; VLC/Steam don't). When true, MoveWindowToScreen schedules a reactive check
    // that corrects the window back to Bounds if anything changes it away - see
    // DpiCorrectionScheduler.ScheduleDpiCompensationCheck and ISSUES.md's resolved DPI-sizing
    // history for why this replaced an earlier approach that tried to pre-guess a
    // DPI-compensated size instead.
    private static (Rectangle Bounds, bool WatchForSelfResize) DetermineWindowBounds(IntPtr hwnd, Screen target)
    {
        var fallbackSize = new Size(WindowWidth, WindowHeight);

        if (!GetWindowRect(hwnd, out RECT r)) return (WindowPlacement.Centered(target.WorkingArea, fallbackSize), false);

        Rectangle currentBounds = ToRectangle(r);
        Screen[] screens = Screen.AllScreens;
        var layout = new MonitorLayout(Array.ConvertAll(screens, s => s.WorkingArea));
        int sourceIndex = layout.IndexOfMonitorShowing(currentBounds);
        if (sourceIndex < 0) return (WindowPlacement.Centered(target.WorkingArea, fallbackSize), false); // no monitors at all

        Rectangle sourceWorkingArea = layout[sourceIndex];

        // The actual sizing decision (proportional size, clamped to the target monitor) is
        // WindowPlacement.DetermineTargetSize, in WindowMover.Core where it can be unit tested.
        Size size = WindowPlacement.DetermineTargetSize(sourceWorkingArea, currentBounds.Size, target.WorkingArea);
        Rectangle bounds = WindowPlacement.ProportionalPosition(sourceWorkingArea, currentBounds, target.WorkingArea, size);

        bool watchForSelfResize = CrossesRealDpiBoundary(hwnd, target.WorkingArea);
        return (bounds, watchForSelfResize);
    }

    // Is the target window per-monitor-DPI-aware (only those get WM_DPICHANGED and might
    // resize themselves in response), and does this move actually cross a DPI boundary?
    // Returns false - don't bother watching - if either can't be determined.
    private static bool CrossesRealDpiBoundary(IntPtr hwnd, Rectangle targetMonitorBounds)
    {
        int awareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd));
        if (awareness != DPI_AWARENESS_PER_MONITOR_AWARE) return false;

        // Read the source monitor before the window moves - MonitorFromWindow still
        // reflects where it currently is at this point in the call chain.
        IntPtr sourceMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        RECT targetRect = ToRect(targetMonitorBounds);
        IntPtr targetMonitor = MonitorFromRect(ref targetRect, MONITOR_DEFAULTTONEAREST);

        if (GetDpiForMonitor(sourceMonitor, MDT_EFFECTIVE_DPI, out uint sX, out uint sY) != 0) return false;
        if (GetDpiForMonitor(targetMonitor, MDT_EFFECTIVE_DPI, out uint tX, out uint tY) != 0) return false;

        return sX != tX || sY != tY;
    }

    // Cycles a window to the next monitor in the screen array
    public static void MoveWindowToNextScreen(IntPtr hwnd)
    {
        if (!IsSafeMovableWindow(hwnd)) return;
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

        MoveWindowToScreen(hwnd, screens[next]);
    }

    // Makes a window invisible without telling Windows it is hidden: a fully transparent
    // layered window still counts as visible, so it keeps its taskbar button, its Z-order and
    // its focus, and the shell never sees anything happen. Returns whether it worked - the
    // caller must not assume the window is hidden if it did not.
    //
    // Refused for a window that is already layered. Such a window is managing its own
    // transparency (per-pixel alpha, a fade, a custom shape), and there is no way to set an
    // alpha here and hand back whatever it had: the old value is not readable in any form
    // this could restore. Those windows keep the visible flash instead, which is a far
    // smaller harm than leaving an app's own transparency permanently altered.
    private static bool TryHideByTransparency(IntPtr hwnd)
    {
        uint style = GetWindowLong(hwnd, GWL_EXSTYLE);
        if ((style & WS_EX_LAYERED) != 0) return false;

        if (SetWindowLong(hwnd, GWL_EXSTYLE, (int)(style | WS_EX_LAYERED)) == 0)
        {
            DebugLog.Write($"Hide [{DebugLog.DescribeWindowProcess(hwnd)}]: could not add WS_EX_LAYERED, win32 error {Marshal.GetLastWin32Error()}");
            return false;
        }

        if (SetLayeredWindowAttributes(hwnd, 0, AlphaTransparent, LWA_ALPHA)) return true;

        // Half-applied is worse than not applied: the style is on but the window is still
        // opaque, so put it back rather than leave a window layered for no reason.
        DebugLog.Write($"Hide [{DebugLog.DescribeWindowProcess(hwnd)}]: could not set alpha, win32 error {Marshal.GetLastWin32Error()}");
        SetWindowLong(hwnd, GWL_EXSTYLE, (int)style);
        return false;
    }

    // Undoes TryHideByTransparency: opaque again, and WS_EX_LAYERED taken back off, since
    // this app added it and a window left layered composites differently from one that never
    // was. Only ever called for a window TryHideByTransparency returned true for.
    public static void RevealFromTransparency(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return;

        SetLayeredWindowAttributes(hwnd, 0, AlphaOpaque, LWA_ALPHA);
        uint style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (int)(style & ~WS_EX_LAYERED));
    }

    // A move that silently does nothing looks, from the outside, exactly like a gesture that
    // never fired at all - and the two have completely different causes. So a move that
    // Windows refuses says so, with the error code: access denied here means UIPI (the target
    // window belongs to a higher-integrity process than this one) and no retry will help.
    private static void ReportMoveOutcome(IntPtr hwnd, bool applied, string what)
    {
        if (applied) return;
        DebugLog.Write($"Move FAILED [{DebugLog.DescribeWindowProcess(hwnd)}]: {what} returned false, win32 error {Marshal.GetLastWin32Error()}");
    }

    // Defers BringToFront by one short beat - see BringToFrontDelayMs for why it can't just
    // be called inline. A WinForms timer keeps this on the app's message-loop thread (the
    // same thread the mouse hook runs on), so nothing here needs to be thread-safe.
    private static void BringToFrontAfterClickLands(IntPtr hwnd)
    {
        var timer = new System.Windows.Forms.Timer { Interval = BringToFrontDelayMs };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            timer.Dispose();
            BringToFront(hwnd);
        };
        timer.Start();
    }

    // Raises a window to the top of the Z-order and gives it focus. Moving a window onto a
    // monitor that already has windows on it otherwise leaves it wherever it was in the
    // Z-order - which, for a window that wasn't in front to begin with, means it lands
    // behind them and looks like nothing happened.
    //
    // Windows' foreground lock normally refuses SetForegroundWindow from a background
    // process like this one (it returns false and does nothing but flash the taskbar
    // button). The documented exception is a thread whose input queue is attached to the
    // current foreground thread's, so that's the fallback. The attach is released again
    // immediately: while attached the two threads share an input queue, and staying that
    // way any longer than the one call risks this app's input being held up by another
    // app's stalled message loop.
    public static void BringToFront(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return;

        IntPtr foreground = GetForegroundWindow();
        if (foreground == hwnd) return; // already in front - touch nothing

        string process = DebugLog.DescribeWindowProcess(hwnd);

        // Z-order first, and on its own: this is not subject to the foreground lock, so even
        // when activation below is refused the window is at least visible on top of the
        // others rather than buried behind them.
        SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        if (SetForegroundWindow(hwnd))
        {
            DebugLog.Write($"Bring to front [{process}]: activated directly");
            return;
        }

        uint ourThread = GetCurrentThreadId();
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        if (foregroundThread == 0 || foregroundThread == ourThread)
        {
            DebugLog.Write($"Bring to front [{process}]: refused, raised only (no other foreground thread to attach to)");
            return;
        }

        if (!AttachThreadInput(ourThread, foregroundThread, true))
        {
            DebugLog.Write($"Bring to front [{process}]: refused, raised only (input attach failed)");
            return;
        }

        bool activated = SetForegroundWindow(hwnd);
        AttachThreadInput(ourThread, foregroundThread, false);
        DebugLog.Write($"Bring to front [{process}]: activated via input attach={activated}");
    }
}
