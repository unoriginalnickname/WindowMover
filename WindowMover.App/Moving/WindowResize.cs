using System.Runtime.InteropServices;
using WindowMover.Core;
using static NativeMethods;

// Ctrl+Shift held and the wheel turned: the focused window grows or shrinks in place, on the
// monitor it is already on. How much, and where it ends up, is WindowScaling's decision (in
// WindowMover.Core); this is the Win32 side of it.
internal static class WindowResize
{
    // The resize is posted to the window's own thread (SWP_ASYNCWINDOWPOS) rather than waited
    // on. This runs inside the low-level mouse hook, once per wheel event, and a fast spin is
    // dozens of them: waiting on each for an app to repaint at its new size - VS Code takes
    // a while - would hold up every mouse event on the system behind it.
    //
    // Posting means the window has usually not caught up by the next wheel event, so reading
    // its bounds then would scale from a stale size and swallow steps. So a run of wheel
    // events on the same window scales from the bounds last asked for instead. A pause ends
    // the run, after which the window's real bounds are read again - that picks up anything
    // the app or the user did to it in between, including refusing a size it would not go to.
    private const long RunGapMilliseconds = 400;
    private static IntPtr runWindow;
    private static Rectangle runBounds;
    private static long runLastEvent;

    // The caller has already checked the window may be moved, in the same instant - it needs
    // to know before deciding whether to swallow the wheel event.
    public static void Scale(IntPtr hwnd, int wheelDelta)
    {
        long now = Environment.TickCount64;
        bool continuesRun = hwnd == runWindow && now - runLastEvent < RunGapMilliseconds;
        runLastEvent = now;

        Rectangle workingArea = Screen.FromHandle(hwnd).WorkingArea;
        Rectangle current;

        if (continuesRun)
            current = runBounds;
        else
        {
            // A correction still pending from an earlier move would put back the size it was
            // policing the moment this one lands, so settle it first - the same reason
            // WindowMove.ToMonitor does.
            MoveSettleWatcher.FinishPendingWatch(hwnd);

            if (IsZoomed(hwnd))
            {
                // A maximized window can only get smaller: scrolling down takes it out of the
                // maximized state and shrinks it from the full working area, as if it had been
                // sized to fill the screen. Scrolling up has nowhere to go.
                if (wheelDelta >= 0) return;
                ShowWindow(hwnd, SW_RESTORE);
                current = workingArea;
            }
            else if (GetWindowRect(hwnd, out RECT r))
                current = ToRectangle(r);
            else
                return; // window gone
        }

        Rectangle bounds = WindowScaling.Scaled(current, workingArea, wheelDelta);
        runWindow = hwnd;
        runBounds = bounds;

        if (!continuesRun)
            DebugLog.Write($"Resize [{DebugLog.DescribeWindowProcess(hwnd)}]: hwnd={hwnd}, {current} -> {bounds} (delta {wheelDelta})");

        if (!SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS))
            DebugLog.Write($"Resize FAILED [{DebugLog.DescribeWindowProcess(hwnd)}]: SetWindowPos to {bounds} returned false, win32 error {Marshal.GetLastWin32Error()}");
    }
}
