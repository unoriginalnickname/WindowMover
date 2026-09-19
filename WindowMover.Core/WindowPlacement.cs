using System.Drawing;

namespace WindowMover.Core;

public static class WindowPlacement
{
    // Where a window lands on the target monitor: as close as possible to the same relative
    // position it had on the source monitor (independent per axis, same idea as
    // ProportionalSize), but always adjusted to keep it fully on screen when its size
    // allows - reproducing an off-screen position is never acceptable just because it
    // matches where the window happened to be.
    public static Rectangle ProportionalPosition(Rectangle sourceMonitorBounds, Rectangle currentWindowBounds, Rectangle targetMonitorBounds, Size windowSize)
    {
        double xRatio = (double)(currentWindowBounds.X - sourceMonitorBounds.X) / sourceMonitorBounds.Width;
        double yRatio = (double)(currentWindowBounds.Y - sourceMonitorBounds.Y) / sourceMonitorBounds.Height;

        int desiredX = targetMonitorBounds.X + (int)Math.Round(targetMonitorBounds.Width * xRatio);
        int desiredY = targetMonitorBounds.Y + (int)Math.Round(targetMonitorBounds.Height * yRatio);

        // Too wide for the monitor: hang off both side edges evenly, same as Centered - no
        // position keeps the whole width in view, and neither edge matters more than the
        // other here.
        int x = windowSize.Width >= targetMonitorBounds.Width
            ? targetMonitorBounds.X + (targetMonitorBounds.Width - windowSize.Width) / 2
            : Math.Clamp(desiredX, targetMonitorBounds.X, targetMonitorBounds.X + targetMonitorBounds.Width - windowSize.Width);

        // Too tall for the monitor: anchor to the top instead of centring. The title bar -
        // and every control on it - lives along the top edge, so keeping that reachable
        // matters far more than the bottom edge, which can hang off screen instead.
        int y = windowSize.Height >= targetMonitorBounds.Height
            ? targetMonitorBounds.Y
            : Math.Clamp(desiredY, targetMonitorBounds.Y, targetMonitorBounds.Y + targetMonitorBounds.Height - windowSize.Height);

        return new Rectangle(x, y, windowSize.Width, windowSize.Height);
    }

    // Centers a window of the given size on the monitor - used only when there's no current
    // position to preserve (the window's bounds or monitor couldn't be read), so there's
    // nothing for ProportionalPosition to work from.
    public static Rectangle Centered(Rectangle monitorBounds, Size windowSize)
    {
        int x = monitorBounds.X + (monitorBounds.Width - windowSize.Width) / 2;
        int y = monitorBounds.Y + (monitorBounds.Height - windowSize.Height) / 2;

        return new Rectangle(x, y, windowSize.Width, windowSize.Height);
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

    // Never let a computed size exceed the monitor it's landing on. CompensateForTargetDpiResponse
    // (Program.cs) deliberately inflates the size it hands back, betting that the target
    // app's own DPI-change handling will shrink it back down a moment later - a bet that
    // doesn't pay off for every app, and for a large-enough source window crossing onto a
    // lower-DPI monitor, the inflated size can come out bigger than the monitor itself. A
    // window slightly off from the "ideal" compensated size is fine; a window bigger than
    // the screen it's on is not.
    public static Size ClampToMonitor(Size size, Rectangle monitorBounds) =>
        new Size(
            Math.Min(size.Width, monitorBounds.Width),
            Math.Min(size.Height, monitorBounds.Height));

    // The full non-maximized-move sizing decision in one testable place: proportional size,
    // clamped to the target monitor. See ISSUES.md's resolved DPI-sizing history for why this
    // used to also include a DPI-ratio "bet" (deliberately inflating the size, betting a
    // per-monitor-DPI-aware app would shrink it back on its own) - that approach is retired.
    // A per-monitor-DPI-aware app (Chrome, Explorer) auto-resizes itself the moment it detects
    // a DPI change regardless of what size it's handed, so betting on a guessed value never
    // avoided needing a reactive correction step anyway; setting the plain correct size
    // directly and reactively correcting *any* app that changes it away from that (Program.cs's
    // DPI-correction fallback) is simpler, needs no DPI-ratio math at all, and additionally
    // means an app that never touches its size (VLC, Steam) lands correct immediately with no
    // correction step, instead of always paying for one.
    public static Size DetermineTargetSize(Rectangle sourceWorkingArea, Size currentSize, Rectangle targetWorkingArea) =>
        ClampToMonitor(ProportionalSize(sourceWorkingArea, currentSize, targetWorkingArea), targetWorkingArea);
}
