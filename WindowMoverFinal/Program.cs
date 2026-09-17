using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.IO;
using WindowMover.Core;

// Window Mover - A system tray utility that moves windows between monitors
// Controls: Mouse4/Mouse5 + Mouse3 (middle click) to move windows
//
// This file is the Windows half of the app: the P/Invoke declarations, the mouse hook and
// the tray icon. The decisions - which window may be moved, which monitor is next, where
// on that monitor the window goes, what a button combo means - live in WindowMover.Core,
// which knows nothing about Win32 and can therefore be tested.
class Program
{
    // --------------------------- Constants ---------------------------
    // Windows Hook Constants
    private const int WH_MOUSE_LL = 14;              // Low-level mouse hook identifier
    private const int WM_MBUTTONDOWN = 0x0207;       // Middle mouse button pressed
    private const int WM_XBUTTONDOWN = 0x020B;       // Extra mouse button (4 or 5) pressed
    private const int WM_XBUTTONUP = 0x020C;         // Extra mouse button (4 or 5) released
    private const int XBUTTON1 = 0x0001;             // Mouse button 4 identifier
    private const int XBUTTON2 = 0x0002;             // Mouse button 5 identifier

    // Window Management Constants
    private const int SW_RESTORE = 9;                // Restore window from maximized/minimized
    private const int SW_MINIMIZE = 6;               // Minimize window
    private const int SW_MAXIMIZE = 3;               // Maximize window
    private const uint SWP_NOZORDER = 0x0004;        // Don't change Z-order when repositioning
    private const uint SWP_NOACTIVATE = 0x0010;      // Don't activate window when repositioning

    // Application State
    private static IntPtr hookId;                    // Handle to the mouse hook
    private static NotifyIcon trayIcon;              // System tray icon

    // Remembers which extra buttons are pressed and which window they grabbed
    private static readonly ButtonComboTracker buttons = new();

    // Adjustable window size - default dimensions for moved windows
    private static int WindowWidth = 800;
    private static int WindowHeight = 600;

    // --------------------------- Structs ---------------------------
    // Structure for screen coordinates
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    // Structure for window rectangle (bounds)
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    // A window's placement state - used to move a maximized window directly onto another
    // monitor in one call instead of restore -> move -> maximize as three separate calls.
    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
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
    private struct MSLLHOOKSTRUCT
    { //p.invoke site says it must be ints and not uints
        public POINT pt;           // Mouse cursor position
        public uint mouseData;     // Extra info (includes which X button)
        public uint flags;         // Event flags
        public uint time;          // Timestamp
        public IntPtr dwExtraInfo; // Additional data
    }

    // --------------------------- P/Invoke ---------------------------
    // Delegate for mouse hook callback function
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Windows API function imports

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr dpiContext);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    // Window style constants
    private const int GWL_EXSTYLE = -20;             // Extended window style index
    private const uint WS_EX_TOOLWINDOW = 0x00000080; // Tool window style (skip taskbar)

    // DPI query constants
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const int DPI_AWARENESS_PER_MONITOR_AWARE = 2;

    // --------------------------- Main ---------------------------
    [STAThread]
    static void Main()
    {
        // Must run before any window handle is created (including the "already running"
        // MessageBox below) - once one exists, the DPI mode is locked in. Without this the
        // app defaults to SystemAware, which reports monitor/window sizes in a single
        // shared logical space rather than each monitor's true physical pixels - on a
        // multi-monitor setup where monitors have different Windows scaling percentages,
        // that makes a moved window's size come out wrong on whichever monitor's scale
        // differs from the system's.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Ensure only one instance of the application runs at a time
        using var mutex = new System.Threading.Mutex(true, "WindowMover_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Window Mover is already running.", "Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Install low-level mouse hook to intercept all mouse events
        hookId = SetWindowsHookEx(WH_MOUSE_LL, HookCallback, IntPtr.Zero, 0);

        // Create system tray context menu
        var trayMenu = new ContextMenuStrip();

        // "Start with Windows" toggle menu item
        var startupItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleStartup((ToolStripMenuItem)s))
        { Checked = IsStartupEnabled() };

        // "Set Window Size" menu item - allows customizing default window dimensions
        var sizeItem = new ToolStripMenuItem("Set Window Size", null, (s, e) =>
        {
            try
            {
                // Prompt user for width and height
                string widthStr = Microsoft.VisualBasic.Interaction.InputBox("Enter window width:", "Set Window Size", WindowWidth.ToString());
                string heightStr = Microsoft.VisualBasic.Interaction.InputBox("Enter window height:", "Set Window Size", WindowHeight.ToString());

                // Validate and update dimensions
                if (int.TryParse(widthStr, out int w) && int.TryParse(heightStr, out int h))
                {
                    if (w > 0 && h > 0)
                    {
                        WindowWidth = w;
                        WindowHeight = h;
                        trayIcon.ShowBalloonTip(1000, "Window Mover", $"Window size set to {w}x{h}", ToolTipIcon.Info);
                    }
                }
            }
            catch { }
        });

        // Build the context menu
        trayMenu.Items.Add(startupItem);
        trayMenu.Items.Add(sizeItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("About", null, (s, e) => MessageBox.Show(
            "Window Mover\n\n" +
            "• Mouse4 + Mouse3 (or Mouse5 + Mouse3) = Cycle to next monitor\n" +
            "• Mouse4 + Mouse5 + Mouse3 = Move to cursor's monitor\n\n" +
            $"• Current window size: {WindowWidth}x{WindowHeight}",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (s, e) => { trayIcon.Visible = false; Application.Exit(); });

        // Create and display system tray icon
        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // Replace with your turtle icon if desired
            ContextMenuStrip = trayMenu,
            Text = "Window Mover",
            Visible = true
        };

        // After the tray icon exists, perform cleanup and notify user if entries were removed
        var cleanupResult = CleanupOldRunEntries();
        if (cleanupResult.RemovedCount > 0)
        {
            string msg = cleanupResult.RemovedCount == 1
                ? $"Removed 1 old startup entry for WindowMover." 
                : $"Removed {cleanupResult.RemovedCount} old startup entries for WindowMover.";
            trayIcon.ShowBalloonTip(5000, "Window Mover", msg, ToolTipIcon.Info);
        }

        // Run the message loop (keeps app alive in system tray)
        Application.Run();

        // Cleanup on exit
        UnhookWindowsHookEx(hookId);
        trayIcon?.Dispose();
    }

    // --------------------------- Hook Callback ---------------------------
    // This function is called for every mouse event system-wide
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
                if (xButton == XBUTTON1) buttons.SideButtonDown(SideButton.Mouse4, WindowToCapture(fg));
                else if (xButton == XBUTTON2) buttons.SideButtonDown(SideButton.Mouse5, WindowToCapture(fg));
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
                if (request.Command == MoveCommand.CursorMonitor)
                {
                    if (GetCursorPos(out POINT p))
                        MoveWindowToScreen(request.Window, Screen.FromPoint(new Point(p.X, p.Y)));
                }
                else if (request.Command == MoveCommand.NextMonitor)
                    MoveWindowToNextScreen(request.Window);
            }
        }
        // Pass the event to the next hook in the chain
        return CallNextHookEx(hookId, nCode, wParam, lParam);
    }

    // --------------------------- Window Helpers ---------------------------
    // The window to hand to the combo tracker: the given one if we're allowed to move it,
    // otherwise nothing
    private static IntPtr WindowToCapture(IntPtr hwnd)
    {
        return IsSafeMovableWindow(hwnd) ? hwnd : IntPtr.Zero;
    }

    // Reads out everything the filter needs to know about a window
    private static WindowSnapshot Describe(IntPtr hwnd)
    {
        // The desktop's icons and wallpaper are children of the Progman window
        IntPtr shellWnd = FindWindow("Progman", null);

        return new WindowSnapshot(
            IsTaskbar: hwnd == FindWindow("Shell_TrayWnd", null),
            IsVisible: IsWindowVisible(hwnd),
            ExtendedStyle: GetWindowLong(hwnd, GWL_EXSTYLE),
            Bounds: GetWindowRect(hwnd, out RECT r) ? ToRectangle(r) : null,
            IsDesktopChild: GetParent(hwnd) == shellWnd);
    }

    // Checks if a window is safe to move (filters out system windows)
    private static bool IsSafeMovableWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        return WindowMoveFilter.IsSafeToMove(Describe(hwnd));
    }

    // Converts a Win32 RECT (edges) into a Rectangle (position + size)
    private static Rectangle ToRectangle(RECT r) =>
        new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    // Moves a window to a specific screen, centered
    private static void MoveWindowToScreen(IntPtr hwnd, Screen target)
    {
        if (!IsSafeMovableWindow(hwnd)) return;

        if (IsZoomed(hwnd))
        {
            MoveMaximizedWindowToScreen(hwnd, target);
            return;
        }

        Size windowSize = DetermineWindowSize(hwnd, target);
        var plan = WindowPlacement.PlanMove(target.Bounds, windowSize);

        SetWindowPos(hwnd, IntPtr.Zero,
            plan.TargetBounds.X, plan.TargetBounds.Y,
            plan.TargetBounds.Width, plan.TargetBounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // Moves a maximized window directly onto the target monitor in one Win32 call, instead
    // of restore -> move -> maximize as three separate calls (see ISSUES.md history: that
    // sequence visibly shrinks the window to its old restored size, jumps it, then grows it
    // back to maximized). SetWindowPlacement lets us set the window's "restored" position
    // to the target monitor's own working area and ask for it maximized in one shot, so
    // Windows never has to show it at any other size in between.
    private static void MoveMaximizedWindowToScreen(IntPtr hwnd, Screen target)
    {
        var primary = Screen.PrimaryScreen;
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };

        if (primary is null || !GetWindowPlacement(hwnd, ref placement))
        {
            MoveMaximizedWindowTheSlowWay(hwnd, target);
            return;
        }

        Rectangle workspaceRect = WindowPlacement.ToWorkspaceCoordinates(target.WorkingArea, primary.WorkingArea.Location);

        placement.showCmd = SW_MAXIMIZE;
        placement.rcNormalPosition = new RECT
        {
            Left = workspaceRect.Left,
            Top = workspaceRect.Top,
            Right = workspaceRect.Right,
            Bottom = workspaceRect.Bottom
        };
        // ptMaxPosition is a real workspace-coordinate point (the window's maximized
        // top-left corner), not a screen-space or "auto" value - MSDN documents it in the
        // same coordinate space as rcNormalPosition. Left over from the OLD monitor, or set
        // to a bogus sentinel, it disagrees with rcNormalPosition about which monitor the
        // window belongs to, which is why the window wasn't showing up correctly.
        placement.ptMaxPosition = new POINT { X = workspaceRect.Left, Y = workspaceRect.Top };

        if (!SetWindowPlacement(hwnd, ref placement))
        {
            MoveMaximizedWindowTheSlowWay(hwnd, target);
            return;
        }

        // SetWindowPlacement updates the window's placement bookkeeping, but since it
        // already reports itself as maximized, a follow-up ShowWindow(SW_MAXIMIZE) alone is
        // treated as "already there" and is a no-op - Windows never actually redraws it at
        // the new placement. A real state transition is needed to force that: minimizing
        // and then maximizing again is exactly the manual workaround that was confirmed to
        // work, so the code does the same thing instead of leaving it to the user.
        ShowWindow(hwnd, SW_MINIMIZE);
        ShowWindow(hwnd, SW_MAXIMIZE);
    }

    // The original restore -> move -> maximize sequence, kept only as a fallback for the
    // rare case GetWindowPlacement/SetWindowPlacement itself fails.
    private static void MoveMaximizedWindowTheSlowWay(IntPtr hwnd, Screen target)
    {
        var plan = WindowPlacement.PlanMove(target.Bounds, new Size(WindowWidth, WindowHeight));

        ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, IntPtr.Zero,
            plan.TargetBounds.X, plan.TargetBounds.Y,
            plan.TargetBounds.Width, plan.TargetBounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);
        ShowWindow(hwnd, SW_MAXIMIZE);
    }

    // The size to move a non-maximized window to, keeping the same percentage of screen it
    // had before so it does not look tiny dragged onto a much bigger monitor or hang off the
    // edges of a much smaller one - see WindowPlacement.ProportionalSize. Falls back to the
    // configured default when the window's current bounds or monitor cannot be determined,
    // same as the app has always done.
    private static Size DetermineWindowSize(IntPtr hwnd, Screen target)
    {
        var fallback = new Size(WindowWidth, WindowHeight);

        if (!GetWindowRect(hwnd, out RECT r)) return fallback;

        Screen[] screens = Screen.AllScreens;
        var layout = new MonitorLayout(Array.ConvertAll(screens, s => s.Bounds));
        int sourceIndex = layout.IndexOfMonitorShowing(ToRectangle(r));
        if (sourceIndex < 0) return fallback; // no monitors at all

        Size size = WindowPlacement.ProportionalSize(layout[sourceIndex], ToRectangle(r).Size, target.Bounds);
        return CompensateForTargetDpiResponse(hwnd, size, target.Bounds);
    }

    // Crossing onto a monitor with a different DPI scale, a per-monitor-DPI-aware target
    // window (most modern apps) gets WM_DPICHANGED and, by default, resizes itself by
    // (targetDpi / sourceDpi) - asynchronously, after this app's own SetWindowPos already
    // returned, silently overriding whatever size was just set. Pre-dividing by that same
    // ratio here cancels it out. This only helps windows using the *default* linear
    // response - at least one real app (Windows Terminal, which keeps its row/column count
    // constant instead) is confirmed to override it with its own logic, so this improves
    // the common case without being a guaranteed fix for every app.
    private static Size CompensateForTargetDpiResponse(IntPtr hwnd, Size intendedSize, Rectangle targetMonitorBounds)
    {
        // Only per-monitor-DPI-aware windows receive WM_DPICHANGED at all - compensating
        // for a window that never reacts to it would introduce an error where none exists.
        int awareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd));
        if (awareness != DPI_AWARENESS_PER_MONITOR_AWARE) return intendedSize;

        // Read the source monitor before the window moves - MonitorFromWindow still
        // reflects where it currently is at this point in the call chain.
        IntPtr sourceMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        RECT targetRect = ToRect(targetMonitorBounds);
        IntPtr targetMonitor = MonitorFromRect(ref targetRect, MONITOR_DEFAULTTONEAREST);

        if (GetDpiForMonitor(sourceMonitor, MDT_EFFECTIVE_DPI, out uint sourceDpiX, out uint sourceDpiY) != 0) return intendedSize;
        if (GetDpiForMonitor(targetMonitor, MDT_EFFECTIVE_DPI, out uint targetDpiX, out uint targetDpiY) != 0) return intendedSize;

        if (sourceDpiX == targetDpiX && sourceDpiY == targetDpiY) return intendedSize; // no DPI boundary crossed

        return new Size(
            (int)Math.Round(intendedSize.Width * (double)sourceDpiX / targetDpiX),
            (int)Math.Round(intendedSize.Height * (double)sourceDpiY / targetDpiY));
    }

    // Converts a Rectangle (position + size) back into a Win32 RECT (edges)
    private static RECT ToRect(Rectangle r) => new RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };

    // Cycles a window to the next monitor in the screen array
    private static void MoveWindowToNextScreen(IntPtr hwnd)
    {
        if (!IsSafeMovableWindow(hwnd)) return;
        if (!GetWindowRect(hwnd, out RECT r)) return;

        // Hand the monitor bounds to the core and let it work out where the window is and
        // where it goes next
        Screen[] screens = Screen.AllScreens;
        var layout = new MonitorLayout(Array.ConvertAll(screens, s => s.Bounds));

        int current = layout.IndexOfMonitorShowing(ToRectangle(r));
        if (!layout.TryGetNextMonitorIndex(current, out int next)) return; // needs at least 2 monitors

        MoveWindowToScreen(hwnd, screens[next]);
    }

    // --------------------------- Startup Helpers ---------------------------
    // Checks if the app is configured to start with Windows
    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            if (key == null) return false;

            var val = key.GetValue("WindowMover") as string;
            if (string.IsNullOrEmpty(val)) return false;

            // Extract the executable path from the stored value. The value may be quoted and/or contain arguments.
            string firstToken = val.Trim();
            if (firstToken.StartsWith("\""))
            {
                // If quoted, find the closing quote
                int endQuote = firstToken.IndexOf('"', 1);
                if (endQuote > 0)
                    firstToken = firstToken.Substring(1, endQuote - 1);
            }
            else
            {
                // Not quoted: take up to first space (arguments may follow)
                int sp = firstToken.IndexOf(' ');
                if (sp > 0) firstToken = firstToken.Substring(0, sp);
            }
            // If the stored path matches the current executable, startup is enabled.
            if (string.Equals(firstToken, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                return true;

            // Otherwise, the Run entry points to a different path. Remove it so the app doesn't falsely report enabled.
            try
            {
                using var writeKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                writeKey?.DeleteValue("WindowMover", false);
            }
            catch { }

            return false;
        }
        catch { return false; }
    }

    // Toggles the "Start with Windows" setting in the registry
    private static void ToggleStartup(ToolStripMenuItem item)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (item.Checked)
            {
                // Remove from startup
                key.DeleteValue("WindowMover", false);
                item.Checked = false;
            }
            else
            {
                // Add to startup. Quote the path to handle spaces and store as a string.
                string exePath = Application.ExecutablePath;
                string quoted = '"' + exePath + '"';
                key.SetValue("WindowMover", quoted, RegistryValueKind.String);
                item.Checked = true;
            }
        }
        catch { }
    }

    // Result of cleanup operation
    private struct CleanupResult { public int RemovedCount; public string[] RemovedNames; }

    // Scans HKCU Run values and removes entries pointing to WindowMover.exe in other locations.
    // Backups removed values under HKCU\SOFTWARE\WindowMover\RemovedRunEntries.
    private static CleanupResult CleanupOldRunEntries()
    {
        var removed = new List<string>();
        try
        {
            string currentExe = Application.ExecutablePath;
            string currentFile = Path.GetFileName(currentExe);

            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return new CleanupResult { RemovedCount = 0, RemovedNames = Array.Empty<string>() };

            foreach (var name in key.GetValueNames())
            {
                try
                {
                    var obj = key.GetValue(name);
                    if (!(obj is string val) || string.IsNullOrWhiteSpace(val)) continue;

                    string firstToken = val.Trim();
                    if (firstToken.StartsWith("\""))
                    {
                        int endQuote = firstToken.IndexOf('"', 1);
                        if (endQuote > 0)
                            firstToken = firstToken.Substring(1, endQuote - 1);
                    }
                    else
                    {
                        int sp = firstToken.IndexOf(' ');
                        if (sp > 0) firstToken = firstToken.Substring(0, sp);
                    }

                    string file = Path.GetFileName(firstToken);
                    if (!string.Equals(file, currentFile, StringComparison.OrdinalIgnoreCase)) continue;

                    // If paths match, nothing to do
                    try
                    {
                        if (Path.GetFullPath(firstToken).Equals(Path.GetFullPath(currentExe), StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    catch { /* ignore path parse issues and proceed to remove if filename matched */ }

                    // Backup the old value
                    try
                    {
                        using var backup = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\WindowMover\RemovedRunEntries");
                        if (backup != null)
                        {
                            string backupName = name;
                            int i = 1;
                            while (backup.GetValue(backupName) != null)
                                backupName = name + "_" + i++;
                            backup.SetValue(backupName, val, RegistryValueKind.String);
                        }
                    }
                    catch { }

                    // Remove the stale run entry
                    key.DeleteValue(name, false);
                    removed.Add(name);
                }
                catch { }
            }
        }
        catch { }

        return new CleanupResult { RemovedCount = removed.Count, RemovedNames = removed.ToArray() };
    }
}