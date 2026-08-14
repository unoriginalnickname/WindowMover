using System.Drawing;

namespace WindowMover.Core;

// The steps needed to move one window, worked out ahead of the actual Win32 calls.
//
// A maximized window cannot simply be repositioned - Windows keeps it pinned to the
// monitor it is maximized on. It has to be restored first, moved, then maximized again
// on the new monitor. RestoreBeforeMove and MaximizeAfterMove say when to do that.
public readonly record struct WindowMovePlan(
    Rectangle TargetBounds,
    bool RestoreBeforeMove,
    bool MaximizeAfterMove);

public static class WindowPlacement
{
    // Work out where a window should end up on the given monitor.
    //
    // Moved windows always get the configured size (default 800x600) and are centred on
    // the monitor, so the window's own current size does not come into it. A window
    // larger than the monitor ends up hanging off both edges evenly, which is what
    // centring means and matches what the app has always done.
    public static WindowMovePlan PlanMove(Rectangle monitorBounds, Size windowSize, bool isMaximized)
    {
        int x = monitorBounds.X + (monitorBounds.Width - windowSize.Width) / 2;
        int y = monitorBounds.Y + (monitorBounds.Height - windowSize.Height) / 2;

        return new WindowMovePlan(
            new Rectangle(x, y, windowSize.Width, windowSize.Height),
            RestoreBeforeMove: isMaximized,
            MaximizeAfterMove: isMaximized);
    }
}
