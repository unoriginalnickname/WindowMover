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
    // Work out where a window should end up on the given monitor, at the given size.
    //
    // The window is centred on the monitor; a window larger than the monitor ends up
    // hanging off both edges evenly, which is what centring means. Where windowSize comes
    // from is the caller's decision - see ProportionalSize below for the usual case.
    public static WindowMovePlan PlanMove(Rectangle monitorBounds, Size windowSize, bool isMaximized)
    {
        int x = monitorBounds.X + (monitorBounds.Width - windowSize.Width) / 2;
        int y = monitorBounds.Y + (monitorBounds.Height - windowSize.Height) / 2;

        return new WindowMovePlan(
            new Rectangle(x, y, windowSize.Width, windowSize.Height),
            RestoreBeforeMove: isMaximized,
            MaximizeAfterMove: isMaximized);
    }

    // The size a window should become when it moves from one monitor to another, so it
    // keeps occupying the same percentage of screen it did before. Plain pixel size (what
    // drag-and-drop keeps) looks wrong once the two monitors have different resolutions -
    // a window sized for a small monitor looks tiny dragged onto a much bigger one, and
    // hangs off the edges the other way round. Width and height scale independently
    // against each axis of the target monitor, so a window's own aspect ratio only
    // changes if the two monitors' aspect ratios differ from each other.
    public static Size ProportionalSize(Rectangle sourceMonitorBounds, Size currentWindowSize, Rectangle targetMonitorBounds)
    {
        double widthRatio = (double)currentWindowSize.Width / sourceMonitorBounds.Width;
        double heightRatio = (double)currentWindowSize.Height / sourceMonitorBounds.Height;

        return new Size(
            (int)Math.Round(targetMonitorBounds.Width * widthRatio),
            (int)Math.Round(targetMonitorBounds.Height * heightRatio));
    }
}
