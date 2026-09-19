using System.Runtime.InteropServices;

// The raw Win32 surface: P/Invoke declarations, the structs they marshal, and the
// constants those calls need. Nothing here knows what a "move" or a "combo" is -
// see WindowMoveActions and MouseHook for that.
internal static class NativeMethods
{
    // Windows Hook Constants
    public const int WH_MOUSE_LL = 14;              // Low-level mouse hook identifier
    public const int WM_MBUTTONDOWN = 0x0207;        // Middle mouse button pressed
    public const int WM_XBUTTONDOWN = 0x020B;        // Extra mouse button (4 or 5) pressed
    public const int WM_XBUTTONUP = 0x020C;          // Extra mouse button (4 or 5) released
    public const int XBUTTON1 = 0x0001;              // Mouse button 4 identifier
    public const int XBUTTON2 = 0x0002;              // Mouse button 5 identifier

    // Window Management Constants
    public const int SW_HIDE = 0;                    // Hide window (no taskbar flash, no destroy)
    public const int SW_RESTORE = 9;                 // Restore window from maximized/minimized
    public const int SW_MINIMIZE = 6;                // Minimize window
    public const int SW_MAXIMIZE = 3;                // Maximize window
    public const int SW_SHOWNA = 8;                  // Show window in its current state, without activating it
    public const uint SWP_NOZORDER = 0x0004;         // Don't change Z-order when repositioning
    public const uint SWP_NOACTIVATE = 0x0010;       // Don't activate window when repositioning
    public const uint SWP_NOSIZE = 0x0001;           // Keep the window's current size
    public const uint SWP_NOMOVE = 0x0002;           // Keep the window's current position
    public static readonly IntPtr HWND_TOP = IntPtr.Zero; // Top of the non-topmost Z-order

    // Window style constants
    public const int GWL_EXSTYLE = -20;              // Extended window style index
    public const uint WS_EX_TOOLWINDOW = 0x00000080; // Tool window style (skip taskbar)

    // DPI query constants
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;
    public const int DPI_AWARENESS_PER_MONITOR_AWARE = 2;

    // WinEvent constants - used to notice when a user starts dragging a window's own
    // move/resize border, as distinct from a program calling SetWindowPos on it.
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    // Fires the instant a window's bounds actually change, for any reason (ours or the
    // app's own) - lets DpiCorrectionScheduler react immediately instead of polling on a guess.
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const int OBJID_WINDOW = 0;

    // --------------------------- Structs ---------------------------
    // Structure for screen coordinates
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    // Structure for window rectangle (bounds)
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    // A window's placement state - used to move a maximized window directly onto another
    // monitor in one call instead of restore -> move -> maximize as three separate calls.
    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    // Structure for low-level mouse hook data
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    { //p.invoke site says it must be ints and not uints
        public POINT pt;           // Mouse cursor position
        public uint mouseData;     // Extra info (includes which X button)
        public uint flags;         // Event flags
        public uint time;          // Timestamp
        public IntPtr dwExtraInfo; // Additional data
    }

    // --------------------------- P/Invoke ---------------------------
    // Delegate for mouse hook callback function
    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Delegate for WinEvent callback function (EVENT_SYSTEM_MOVESIZESTART watcher)
    public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    // Momentarily joins two threads' input queues - the documented way out of the foreground
    // lock that otherwise refuses SetForegroundWindow from a background process like this one.
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern uint GetWindowLong(IntPtr hWnd, int nIndex);
    // lpWindowName is genuinely optional in the Win32 API (null matches any title for the given class).
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);
    [DllImport("Shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr dpiContext);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // --------------------------- Conversions ---------------------------
    // Converts a Win32 RECT (edges) into a Rectangle (position + size)
    public static Rectangle ToRectangle(RECT r) =>
        new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    // Converts a Rectangle (position + size) back into a Win32 RECT (edges)
    public static RECT ToRect(Rectangle r) => new RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
}
