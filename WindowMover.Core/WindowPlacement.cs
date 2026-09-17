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
}
