using System.Drawing;

namespace WindowMover.Core;

// Where a non-maximized window should end up, worked out ahead of the actual Win32 call.
// Maximized windows don't go through this - see MoveMaximizedWindowToScreen in Program.cs,
// which repositions them directly via SetWindowPlacement instead of centring a size.
public readonly record struct WindowMovePlan(Rectangle TargetBounds);

public static class WindowPlacement
{
    // Work out where a window should end up on the given monitor, at the given size.
    //
    // The window is centred on the monitor; a window larger than the monitor ends up
    // hanging off both edges evenly, which is what centring means. Where windowSize comes
    // from is the caller's decision - see ProportionalSize below for the usual case.
    public static WindowMovePlan PlanMove(Rectangle monitorBounds, Size windowSize)
    {
        int x = monitorBounds.X + (monitorBounds.Width - windowSize.Width) / 2;
        int y = monitorBounds.Y + (monitorBounds.Height - windowSize.Height) / 2;

        return new WindowMovePlan(new Rectangle(x, y, windowSize.Width, windowSize.Height));
    }

    // SetWindowPlacement's rcNormalPosition is in "workspace coordinates", not the screen
    // coordinates the rest of this app uses: it's offset by the primary monitor's own work
    // area origin, which is (0,0) in the common case (taskbar on the bottom or right) and
    // non-zero only when the taskbar sits on the primary monitor's top or left edge.
    public static Rectangle ToWorkspaceCoordinates(Rectangle screenRect, Point primaryWorkAreaOrigin) =>
        new Rectangle(
            screenRect.X - primaryWorkAreaOrigin.X,
            screenRect.Y - primaryWorkAreaOrigin.Y,
            screenRect.Width,
            screenRect.Height);

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
