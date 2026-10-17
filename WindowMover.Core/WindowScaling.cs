using System.Drawing;

namespace WindowMover.Core;

public static class WindowScaling
{
    // One notch of a standard wheel. Smooth-scrolling wheels and touchpads send fractions of
    // it, many times over, so the scaling works in fractions of a notch rather than counting
    // whole ones - a delta of 30 is a quarter of a step, not nothing.
    public const int WheelDeltaPerNotch = 120;

    // How much one notch grows a window: 10% bigger up, 10% smaller down. Applied as a power,
    // so a notch up and a notch down cancel out instead of drifting.
    public const double ScalePerNotch = 1.1;

    // Below this a window stops being usable, and many apps refuse to go smaller anyway.
    public static readonly Size MinimumSize = new(320, 200);

    // The bounds a window gets for a wheel movement while Ctrl+Shift is held: scaled around its
    // own centre, so it grows and shrinks in place instead of from its top-left corner.
    // Never bigger than the working area of its monitor, and kept fully on it - the same rule
    // WindowPlacement applies after a move. Width and height scale together, so a window keeps
    // its shape until one side reaches the edge of the screen; that side stops there while the
    // other carries on.
    public static Rectangle Scaled(Rectangle window, Rectangle workingArea, int wheelDelta)
    {
        double factor = Math.Pow(ScalePerNotch, (double)wheelDelta / WheelDeltaPerNotch);

        // A window already under the minimum is left at its size rather than grown by a
        // scroll that asked it to shrink.
        int width = Math.Clamp((int)Math.Round(window.Width * factor), Math.Min(Math.Min(MinimumSize.Width, window.Width), workingArea.Width), workingArea.Width);
        int height = Math.Clamp((int)Math.Round(window.Height * factor), Math.Min(Math.Min(MinimumSize.Height, window.Height), workingArea.Height), workingArea.Height);

        return WindowPlacement.ResizedAroundCentre(window, new Size(width, height), workingArea);
    }
}
