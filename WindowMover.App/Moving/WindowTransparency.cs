using System.Runtime.InteropServices;
using static NativeMethods;

// Everything needed to put a window's appearance back exactly as it was found. Which of
// the two cases it is matters: a window this app made layered has to be un-layered again,
// while a window that was already layered has to keep the style and get its own alpha
// back, untouched.
internal readonly record struct TransparencyRestore(bool AppAddedLayeredStyle, uint ColorKey, byte Alpha, uint Flags);

// Hiding a window for the duration of a move, and putting it back afterwards.
//
// Its own file rather than part of WindowMove because both halves of a move use it from
// opposite ends: WindowMove hides the window before it starts, and MoveSettleWatcher
// reveals it whenever the move finally settles. With this in the middle, neither of those
// two has to know about the other.
internal static class WindowTransparency
{
    // User-facing toggle (tray menu). On by default for the cleaner visual result; off
    // trades that back for a visible flash in exchange for never touching the window's
    // appearance at all.
    public static bool HideWhileMoving { get; set; } = true;

    // Makes a window invisible without telling Windows it is hidden: a fully transparent
    // layered window still counts as visible, so it keeps its taskbar button, its Z-order and
    // its focus, and the shell never sees anything happen. Returns how to undo it, or null
    // when the window could not be hidden - the caller must not assume it was.
    //
    // A window that is already layered is not automatically refused. If its transparency came
    // from SetLayeredWindowAttributes, the exact values can be read back and restored, so it
    // is hidden like any other. Only per-pixel alpha (UpdateLayeredWindow) is refused: there
    // is no single alpha to read, and setting one would replace the window's own compositing
    // with a flat value this could never put back. Those windows keep the visible flash,
    // which is a far smaller harm than leaving an app's appearance permanently altered.
    public static TransparencyRestore? TryHide(IntPtr hwnd)
    {
        uint style = GetWindowLong(hwnd, GWL_EXSTYLE);
        string process = DebugLog.DescribeWindowProcess(hwnd);

        if ((style & WS_EX_LAYERED) != 0)
        {
            if (!GetLayeredWindowAttributes(hwnd, out uint key, out byte alpha, out uint flags))
            {
                DebugLog.Write($"Hide [{process}]: already layered with per-pixel alpha, leaving it alone");
                return null;
            }

            if (!SetLayeredWindowAttributes(hwnd, key, AlphaTransparent, flags | LWA_ALPHA))
            {
                DebugLog.Write($"Hide [{process}]: could not set alpha on an already-layered window, win32 error {Marshal.GetLastWin32Error()}");
                return null;
            }

            return new TransparencyRestore(AppAddedLayeredStyle: false, key, alpha, flags);
        }

        if (SetWindowLong(hwnd, GWL_EXSTYLE, (int)(style | WS_EX_LAYERED)) == 0)
        {
            DebugLog.Write($"Hide [{process}]: could not add WS_EX_LAYERED, win32 error {Marshal.GetLastWin32Error()}");
            return null;
        }

        if (SetLayeredWindowAttributes(hwnd, 0, AlphaTransparent, LWA_ALPHA))
            return new TransparencyRestore(AppAddedLayeredStyle: true, 0, AlphaOpaque, LWA_ALPHA);

        // Half-applied is worse than not applied: the style is on but the window is still
        // opaque, so put it back rather than leave a window layered for no reason.
        DebugLog.Write($"Hide [{process}]: could not set alpha, win32 error {Marshal.GetLastWin32Error()}");
        SetWindowLong(hwnd, GWL_EXSTYLE, (int)style);
        return null;
    }

    // Hides the window only if the user left the tray toggle on, so no caller has to check
    // it first. Null either way when nothing was hidden.
    public static TransparencyRestore? TryHideIfEnabled(IntPtr hwnd) =>
        HideWhileMoving ? TryHide(hwnd) : null;

    // Undoes TryHide, using what it reported at the time. A window this app made layered is
    // put back opaque and un-layered, since a window left layered composites differently from
    // one that never was; a window that was already layered gets its own colour key, alpha
    // and flags back and keeps the style it came with.
    public static void Reveal(IntPtr hwnd, TransparencyRestore restore)
    {
        if (!IsWindow(hwnd)) return;

        SetLayeredWindowAttributes(hwnd, restore.ColorKey, restore.Alpha, restore.Flags);
        if (!restore.AppAddedLayeredStyle) return;

        uint style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (int)(style & ~WS_EX_LAYERED));
    }
}
