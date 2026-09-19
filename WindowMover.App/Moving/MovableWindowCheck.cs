using WindowMover.Core;
using static NativeMethods;

// "May this window be moved at all?" - reading a window out of Win32 and asking
// WindowMoveFilter (in WindowMover.Core, where the rule itself is unit-tested) about it.
// Asked twice per gesture: once when a side button goes down and the window is captured,
// and again at click time, because a window can stop being movable in between.
internal static class MovableWindowCheck
{
    // Progman and Shell_TrayWnd are singleton top-level windows that exist for the lifetime
    // of the Explorer shell - looked up once and cached rather than on every move gesture,
    // since the check below runs twice per move and every uncached FindWindow call works
    // against this app's own lightweight/non-invasive goal. Re-looked-up whenever the cached
    // handle is no longer a live window (e.g. Explorer restarting) - a destroyed HWND does
    // not become zero on its own, so a zero-only check would never catch this.
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
    public static IntPtr CaptureIfMovable(IntPtr hwnd)
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

    // Whether a window is safe to move right now (filters out system windows).
    public static bool IsMovable(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        return WindowMoveFilter.IsSafeToMove(Describe(hwnd));
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
}
