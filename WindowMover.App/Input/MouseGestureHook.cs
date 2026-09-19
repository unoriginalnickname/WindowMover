using System.Runtime.InteropServices;
using WindowMover.Core;
using static NativeMethods;

// The gesture: a side button held, then a middle click. This installs the low-level mouse
// hook, which Windows calls for every mouse event system-wide, and hands what it sees to
// ButtonComboTracker (in WindowMover.Core) to say what the combination means. Whatever the
// tracker decides is a move, WindowMove carries out.
internal static class MouseGestureHook
{
    private static IntPtr hookId;

    // SetWindowsHookEx only takes a raw function pointer; nothing else in the app was
    // holding a managed reference to the delegate, so the GC was free to collect it while
    // Windows was still calling through it - an intermittent, hard-to-reproduce crash on a
    // mouse event. Keeping it in this static field roots it for the process's lifetime.
    private static LowLevelMouseProc? hookProc;

    // Remembers which extra buttons are pressed and which window they grabbed
    private static readonly ButtonComboTracker buttons = new();

    public static void Install()
    {
        hookProc = HookCallback;
        hookId = SetWindowsHookEx(WH_MOUSE_LL, hookProc, IntPtr.Zero, 0);
        DebugLog.Write(hookId == IntPtr.Zero
            ? $"Mouse hook FAILED to install: win32 error {Marshal.GetLastWin32Error()}"
            : $"Mouse hook installed: id={hookId}");
    }

    public static void Uninstall()
    {
        UnhookWindowsHookEx(hookId);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            // Parse the mouse event data
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int xButton = (int)(hookStruct.mouseData >> 16); // Extract which X button (4 or 5)

            // Handle extra mouse button press (Mouse 4 or 5)
            if (wParam == (IntPtr)WM_XBUTTONDOWN)
            {
                // Grab the focused window now - by the time the user middle-clicks,
                // something else may have taken focus
                IntPtr fg = GetForegroundWindow();
                IntPtr captured = MovableWindowCheck.CaptureIfMovable(fg);
                // Logged on every side-button press, not only the ones that end in a move:
                // when a gesture does nothing at all, this line is what says whether the hook
                // even saw the press, and which window it grabbed.
                DebugLog.Write($"Side button {(xButton == XBUTTON1 ? "Mouse4" : "Mouse5")} down: foreground={fg} [{DebugLog.DescribeWindowProcess(fg)}], captured={captured}");
                if (xButton == XBUTTON1) buttons.SideButtonDown(SideButton.Mouse4, captured);
                else if (xButton == XBUTTON2) buttons.SideButtonDown(SideButton.Mouse5, captured);
            }
            // Handle extra mouse button release
            else if (wParam == (IntPtr)WM_XBUTTONUP)
            {
                if (xButton == XBUTTON1) buttons.SideButtonUp(SideButton.Mouse4);
                else if (xButton == XBUTTON2) buttons.SideButtonUp(SideButton.Mouse5);
            }
            // Handle middle mouse button click - the tracker decides what the held buttons mean
            else if (wParam == (IntPtr)WM_MBUTTONDOWN)
            {
                var request = buttons.MiddleButtonDown();
                DebugLog.Write($"Middle click: command={request.Command}, window={request.Window}, Mouse4 held={buttons.IsHeld(SideButton.Mouse4)}, Mouse5 held={buttons.IsHeld(SideButton.Mouse5)}");
                if (request.Command == MoveCommand.CursorMonitor)
                {
                    if (GetCursorPos(out POINT p))
                        WindowMove.ToMonitor(request.Window, Screen.FromPoint(new Point(p.X, p.Y)));
                }
                else if (request.Command == MoveCommand.NextMonitor)
                    WindowMove.ToNextMonitor(request.Window);
            }
        }
        // Pass the event to the next hook in the chain
        return CallNextHookEx(hookId, nCode, wParam, lParam);
    }
}
