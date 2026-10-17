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

    // Never let a computed size exceed the monitor it's landing on. ProportionalSize scales by
    // the window's current size over its source monitor, so any window bigger than that
    // monitor's working area comes back bigger than the target's - and a window can be: sized
    // to the full screen bounds rather than the working area, or matched to a source monitor
    // that was only the best guess available for a window straddling two or sitting off-screen
    // entirely. A window slightly off from the ideal proportional size is fine; a window
    // bigger than the screen it's on is not.
    //
    // This also guarded a retired approach that deliberately inflated the size (see
    // DetermineTargetSize below), where the overshoot was far larger and routine.
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
    // directly and reactively correcting *any* app that changes it away from that (the app's
    // MoveSettleWatcher) is simpler, needs no DPI-ratio math at all, and additionally
    // means an app that never touches its size (VLC, Steam) lands correct immediately with no
    // correction step, instead of always paying for one.
    public static Size DetermineTargetSize(Rectangle sourceWorkingArea, Size currentSize, Rectangle targetWorkingArea) =>
        ClampToMonitor(ProportionalSize(sourceWorkingArea, currentSize, targetWorkingArea), targetWorkingArea);

    // The size picked in the tray's "Size after a move" menu, as a percentage of the target
    // monitor's working area on each side - 50 is half as wide and half as tall. Every
    // non-maximized move lands at it, bigger or smaller than the window was, instead of at
    // its proportional size. A maximized window still lands maximized.
    public static Size ShareOfScreen(Rectangle targetWorkingArea, int percent) =>
        new Size(
            (int)Math.Round(targetWorkingArea.Width * percent / 100.0),
            (int)Math.Round(targetWorkingArea.Height * percent / 100.0));

    // The same window at a new size, with its centre kept where it was and then pulled back
    // fully onto the working area if the new size pushed an edge past it. Used when a move
    // lands at a share of the screen - the window lands where its proportional bounds would
    // have been centred, so it still goes to the same part of the screen - and by
    // WindowScaling for the Ctrl+Shift + wheel resize.
    public static Rectangle ResizedAroundCentre(Rectangle bounds, Size size, Rectangle workingArea)
    {
        int x = bounds.X + (bounds.Width - size.Width) / 2;
        int y = bounds.Y + (bounds.Height - size.Height) / 2;
        x = Math.Clamp(x, workingArea.X, Math.Max(workingArea.X, workingArea.Right - size.Width));
        y = Math.Clamp(y, workingArea.Y, Math.Max(workingArea.Y, workingArea.Bottom - size.Height));

        return new Rectangle(x, y, size.Width, size.Height);
    }

    // Ctrl+Shift + Mouse4 or Mouse5: the left or right half of the working area, full height.
    // On an odd width the right half gets the extra pixel, so the two halves always meet
    // exactly in the middle with no gap and no overlap.
    public static Rectangle Half(Rectangle workingArea, bool right)
    {
        int leftWidth = workingArea.Width / 2;
        return right
            ? new Rectangle(workingArea.X + leftWidth, workingArea.Y, workingArea.Width - leftWidth, workingArea.Height)
            : new Rectangle(workingArea.X, workingArea.Y, leftWidth, workingArea.Height);
    }

    // The bounds to hand SetWindowPos so the window's *visible* frame lands on target. Since
    // Windows 10 most windows have an invisible resize border a few pixels wide on the left,
    // right and bottom, which GetWindowRect counts and the eye does not; setting the target
    // directly leaves a gap between the window and the screen edge. windowRect is what
    // GetWindowRect reports and visibleFrame what DWM says is actually drawn, at the window's
    // current size - the border does not change with size, so the difference carries over.
    public static Rectangle IncludingInvisibleBorder(Rectangle target, Rectangle windowRect, Rectangle visibleFrame)
    {
        int left = visibleFrame.Left - windowRect.Left;
        int top = visibleFrame.Top - windowRect.Top;
        int right = windowRect.Right - visibleFrame.Right;
        int bottom = windowRect.Bottom - visibleFrame.Bottom;

        return Rectangle.FromLTRB(target.Left - left, target.Top - top, target.Right + right, target.Bottom + bottom);
    }
}
