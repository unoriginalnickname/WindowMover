using System.Runtime.InteropServices;
using WindowMover.Core;
using static NativeMethods;

// Ctrl+Shift + Mouse4 or Mouse5: the focused window fills the left or right half of the
// monitor it is on, edge to edge and full height. Which half, in pixels, is
// WindowPlacement.Half's decision; this is the Win32 side of it.
internal static class WindowSnap
{
    // The caller has already checked the window may be moved, in the same instant - it needs
    // to know before deciding whether to swallow the button press.
    public static void ToHalf(IntPtr hwnd, bool right)
    {
        // Same reason as WindowResize.Scale: a correction still pending from an earlier move
        // would put back the size it was policing the moment this one lands
        MoveSettleWatcher.FinishPendingWatch(hwnd);

        Screen screen = Screen.FromHandle(hwnd);
        Rectangle half = WindowPlacement.Half(screen.WorkingArea, right);

        // A maximized window ignores new bounds until it is taken out of the maximized state
        if (IsZoomed(hwnd)) ShowWindow(hwnd, SW_RESTORE);

        // Read after the restore, so the border measured is the restored window's
        Rectangle bounds = half;
        if (GetWindowRect(hwnd, out RECT r)
            && DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>()) == 0)
            bounds = WindowPlacement.IncludingInvisibleBorder(half, ToRectangle(r), ToRectangle(frame));

        DebugLog.Write($"Snap [{DebugLog.DescribeWindowProcess(hwnd)}]: hwnd={hwnd} -> {(right ? "right" : "left")} half of {screen.DeviceName}, {half} (set as {bounds})");

        if (!SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS))
            DebugLog.Write($"Snap FAILED [{DebugLog.DescribeWindowProcess(hwnd)}]: SetWindowPos to {bounds} returned false, win32 error {Marshal.GetLastWin32Error()}");

        MoveIndicator.Show(screen, half);
    }
}
