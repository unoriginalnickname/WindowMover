using System.Drawing;

namespace WindowMover.Core;

// The monitors as the app sees them: their bounds in virtual-desktop coordinates, in the
// order Windows reports them. Cycling to "the next monitor" means the next one in this
// order, wrapping back to the first at the end.
public sealed class MonitorLayout
{
    private readonly IReadOnlyList<Rectangle> monitors;

    public MonitorLayout(IReadOnlyList<Rectangle> monitors)
    {
        this.monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
    }

    public int Count => monitors.Count;

    public Rectangle this[int index] => monitors[index];

    // Which monitor is a window "on"? A window can straddle two monitors, so the answer is
    // the monitor showing the most of it. A window that is fully off-screen (it happens -
    // a monitor gets unplugged, or an app restores a saved position) still has to land
    // somewhere, so it falls back to the nearest monitor.
    // Returns -1 only when there are no monitors at all.
    public int IndexOfMonitorShowing(Rectangle windowBounds)
    {
        int best = -1;
        long bestOverlap = -1;
        long bestGap = long.MaxValue;

        for (int i = 0; i < monitors.Count; i++)
        {
            long overlap = OverlapArea(windowBounds, monitors[i]);
            long gap = SquaredGap(windowBounds, monitors[i]);

            // More of the window visible wins. When two monitors show the same amount
            // (usually because they show none of it), the closer one wins.
            if (overlap > bestOverlap || (overlap == bestOverlap && gap < bestGap))
            {
                best = i;
                bestOverlap = overlap;
                bestGap = gap;
            }
        }

        return best;
    }

    // The next monitor in the cycle, wrapping past the last one back to the first.
    // False when there is nothing to cycle to: a single monitor, or a window we could not
    // place on any monitor.
    public bool TryGetNextMonitorIndex(int currentIndex, out int nextIndex)
    {
        nextIndex = -1;
        if (monitors.Count < 2) return false;
        if (currentIndex < 0 || currentIndex >= monitors.Count) return false;

        nextIndex = (currentIndex + 1) % monitors.Count;
        return true;
    }

    // How many pixels of the window the monitor is showing.
    private static long OverlapArea(Rectangle window, Rectangle monitor)
    {
        long width = Math.Min(window.Right, monitor.Right) - Math.Max(window.Left, monitor.Left);
        long height = Math.Min(window.Bottom, monitor.Bottom) - Math.Max(window.Top, monitor.Top);
        if (width <= 0 || height <= 0) return 0;

        return width * height;
    }

    // Squared distance between the two rectangles' nearest edges; 0 when they touch or
    // overlap. Squared because we only ever compare gaps, so the square root is wasted work.
    private static long SquaredGap(Rectangle window, Rectangle monitor)
    {
        long dx = Math.Max(0, Math.Max(monitor.Left - window.Right, window.Left - monitor.Right));
        long dy = Math.Max(0, Math.Max(monitor.Top - window.Bottom, window.Top - monitor.Bottom));

        return dx * dx + dy * dy;
    }
}
