using WindowMover.Core;
using static NativeMethods;

// Where a non-maximized window should land, and whether the move needs watching afterwards.
// The arithmetic itself is WindowPlacement's, in WindowMover.Core where it is unit-tested;
// what is here is only the Win32 reading that feeds it - the window's current bounds, the
// monitors, and the two DPI questions.
internal static class MoveTargetBounds
{
    // Fallback size for the rare case a window's current bounds or monitor can't be read,
    // leaving nothing to size proportionally from. The maximized path has no use for it: it
    // lands on the target monitor's working area.
    private const int FallbackWidth = 800;
    private const int FallbackHeight = 600;

    // The window lands at the same relative position it had on its source monitor, at the
    // same percentage of screen size it had before, so it neither jumps to an unrelated spot
    // nor looks tiny/oversized crossing between differently sized monitors - see
    // WindowPlacement.ProportionalPosition and ProportionalSize. Falls back to a fixed size
    // centered on the target monitor when the window's current bounds or monitor can't be
    // determined, same as the app has always done.
    //
    // WatchForSelfResize is true only when this move crosses a real DPI boundary on a
    // per-monitor-DPI-aware window - the only case where the target app might change this
    // size on its own afterward (Chrome/Explorer auto-resize themselves the moment they detect
    // a DPI change; VLC/Steam don't). When true, WindowMove hands the move to
    // MoveSettleWatcher, which corrects the window back to Bounds if anything changes it away -
    // see ISSUES.md's resolved DPI-sizing history for why this replaced an earlier approach
    // that tried to pre-guess a DPI-compensated size instead.
    public static (Rectangle Bounds, bool WatchForSelfResize) For(IntPtr hwnd, Screen target)
    {
        var fallbackSize = new Size(FallbackWidth, FallbackHeight);

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

        return (bounds, CrossesRealDpiBoundary(hwnd, target.WorkingArea));
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
}
