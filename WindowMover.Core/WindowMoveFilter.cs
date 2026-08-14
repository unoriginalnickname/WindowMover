using System.Drawing;

namespace WindowMover.Core;

// Everything the "should we move this window?" decision needs to know about a window,
// read out of Win32 by the caller so the decision itself stays testable.
public readonly record struct WindowSnapshot(
    // The taskbar itself (Windows calls it the Shell_TrayWnd window).
    bool IsTaskbar,
    bool IsVisible,
    // The extended style bits from GetWindowLong(GWL_EXSTYLE).
    uint ExtendedStyle,
    // The window's bounds, or null if Windows would not tell us (GetWindowRect failed).
    Rectangle? Bounds,
    // True when the window's parent is the desktop shell window (Progman) - that is,
    // it is part of the wallpaper/icon layer rather than a real app window.
    bool IsDesktopChild);

public static class WindowMoveFilter
{
    // Tool windows are the small floating helpers - tooltips, palettes, IME popups. They
    // stay out of the taskbar and are not the thing the user meant to move.
    public const uint WS_EX_TOOLWINDOW = 0x00000080;

    // Anything smaller than this in either direction is almost certainly a bit of system
    // UI rather than a window worth throwing across the desk.
    public const int MinimumMovableSize = 50;

    // Windows we refuse to move, because moving them either does nothing useful or breaks
    // the desktop: the taskbar, tool windows, invisible windows, tiny system UI and the
    // desktop's own icon layer.
    public static bool IsSafeToMove(WindowSnapshot window)
    {
        if (window.IsTaskbar) return false;

        if ((window.ExtendedStyle & WS_EX_TOOLWINDOW) != 0) return false;

        if (!window.IsVisible) return false;

        // If Windows would not give us a size we let it through - a window we cannot
        // measure is not automatically a window we should refuse.
        if (window.Bounds is Rectangle bounds &&
            (bounds.Width < MinimumMovableSize || bounds.Height < MinimumMovableSize)) return false;

        if (window.IsDesktopChild) return false;

        return true;
    }
}
