using System.Runtime.InteropServices;
using WindowMover.Core;
using static NativeMethods;

// The window-movement half: deciding whether a window may be moved, and moving it onto a
// target monitor (maximized or not, with DPI self-correction handled by
// DpiCorrectionScheduler). The decisions about *which* monitor and *what size* live in
// WindowMover.Core, which knows nothing about Win32 and can therefore be tested.
internal static class WindowMoveActions
{
    // Fallback size for a maximized window's remembered restored size, and for the rare
    // case a window's current bounds or monitor can't be read - see DetermineWindowBounds.
    private const int WindowWidth = 800;
    private const int WindowHeight = 600;

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
    // otherwise nothing
    public static IntPtr WindowToCapture(IntPtr hwnd)
    {
        return IsSafeMovableWindow(hwnd) ? hwnd : IntPtr.Zero;
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

    // Moves a window to a specific screen, centered
    public static void MoveWindowToScreen(IntPtr hwnd, Screen target)
    {
        if (!IsSafeMovableWindow(hwnd)) return;

        // Must happen before the IsZoomed check below, not just before DetermineWindowBounds -
        // see ISSUES.md #3. A window can become maximized (by the user, independent of this
        // app, or via a previous WindowMover move) while a DPI correction from an earlier
        // non-maximized move is still pending; resolving it here first - before deciding which
        // move path to take - means ResolvePendingDpiCorrection's own maximized check (below)
        // sees the window's *current* state and skips forcing stale, small bounds onto it,
        // instead of a stale timer doing that later regardless of which path ran.
        DpiCorrectionScheduler.ResolvePendingDpiCorrection(hwnd);

        if (IsZoomed(hwnd))
        {
            MoveMaximizedWindowToScreen(hwnd, target);
            return;
        }

        var (bounds, watchForSelfResize) = DetermineWindowBounds(hwnd, target);

        SetWindowPos(hwnd, IntPtr.Zero,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);

        if (watchForSelfResize)
            DpiCorrectionScheduler.ScheduleDpiCompensationCheck(hwnd, bounds);
    }

    // Moves a maximized window directly onto the target monitor in one Win32 call, instead
    // of restore -> move -> maximize as three separate calls (see ISSUES.md history: that
    // sequence visibly shrinks the window to its old restored size, jumps it, then grows it
    // back to maximized). SetWindowPlacement lets us set the window's "restored" position
    // to the target monitor's own working area and ask for it maximized in one shot, so
    // Windows never has to show it at any other size in between.
    private static void MoveMaximizedWindowToScreen(IntPtr hwnd, Screen target)
    {
        var primary = Screen.PrimaryScreen;
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };

        if (primary is null || !GetWindowPlacement(hwnd, ref placement))
        {
            MoveMaximizedWindowTheSlowWay(hwnd, target);
            return;
        }

        Rectangle workspaceRect = WindowPlacement.ToWorkspaceCoordinates(target.WorkingArea, primary.WorkingArea.Location);

        placement.showCmd = SW_MAXIMIZE;
        placement.rcNormalPosition = new RECT
        {
            Left = workspaceRect.Left,
            Top = workspaceRect.Top,
            Right = workspaceRect.Right,
            Bottom = workspaceRect.Bottom
        };
        // ptMaxPosition is a real workspace-coordinate point (the window's maximized
        // top-left corner), not a screen-space or "auto" value - MSDN documents it in the
        // same coordinate space as rcNormalPosition. Left over from the OLD monitor, or set
        // to a bogus sentinel, it disagrees with rcNormalPosition about which monitor the
        // window belongs to, which is why the window wasn't showing up correctly.
        placement.ptMaxPosition = new POINT { X = workspaceRect.Left, Y = workspaceRect.Top };

        if (!SetWindowPlacement(hwnd, ref placement))
        {
            MoveMaximizedWindowTheSlowWay(hwnd, target);
            return;
        }

        // SetWindowPlacement updates the window's placement bookkeeping, but since it
        // already reports itself as maximized, a follow-up ShowWindow(SW_MAXIMIZE) alone is
        // treated as "already there" and is a no-op - Windows never actually redraws it at
        // the new placement. A real state transition is needed to force that: minimizing
        // and then maximizing again is exactly the manual workaround that was confirmed to
        // work, so the code does the same thing instead of leaving it to the user.
        ShowWindow(hwnd, SW_MINIMIZE);
        ShowWindow(hwnd, SW_MAXIMIZE);
    }

    // The original restore -> move -> maximize sequence, kept only as a fallback for the
    // rare case GetWindowPlacement/SetWindowPlacement itself fails.
    private static void MoveMaximizedWindowTheSlowWay(IntPtr hwnd, Screen target)
    {
        var bounds = WindowPlacement.Centered(target.Bounds, new Size(WindowWidth, WindowHeight));

        ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, IntPtr.Zero,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);
        ShowWindow(hwnd, SW_MAXIMIZE);
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
        if (!layout.TryGetNextMonitorIndex(current, out int next)) return; // needs at least 2 monitors

        MoveWindowToScreen(hwnd, screens[next]);
    }
}
