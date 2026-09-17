using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
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

    // Fallback size for a maximized window's remembered restored size, and for the rare
    // case a window's current bounds or monitor can't be read - see DetermineWindowSize.
    private const int WindowWidth = 800;
    private const int WindowHeight = 600;

    // --------------------------- Diagnostics ---------------------------
    // A lightweight, always-on log for runtime behavior that's hard to reproduce and that unit
    // tests can't cover, since it lives in the untestable Win32 half - e.g. the per-app DPI
    // self-correction quirks in ISSUES.md #3 (VLC/Steam vs. Chrome/Explorer). Best-effort only:
    // a logging failure must never affect a real move.
    private static class DebugLog
    {
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "windowmover-debug.log");

        public static void Write(string message)
        {
            try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n"); }
            catch { /* best-effort only */ }
        }
    }

    // Identifies which app a window belongs to for log messages - without this, log entries
    // for different apps are indistinguishable whenever their windows happen to end up the
    // same size, which cost real time and a wrong conclusion once already (see ISSUES.md #3).
    private static string DescribeWindowProcess(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch { return "unknown"; }
    }

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
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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

        // Build the context menu
        trayMenu.Items.Add(startupItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("About", null, (s, e) => MessageBox.Show(
            "Window Mover\n\n" +
            "• Mouse4 + Mouse3 (or Mouse5 + Mouse3) = Cycle to next monitor\n" +
            "• Mouse4 + Mouse5 + Mouse3 = Move to cursor's monitor",
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

        // Must happen before the IsZoomed check below, not just before DetermineWindowBounds -
        // see ISSUES.md #3. A window can become maximized (by the user, independent of this
        // app, or via a previous WindowMover move) while a DPI correction from an earlier
        // non-maximized move is still pending; resolving it here first - before deciding which
        // move path to take - means ResolvePendingDpiCorrection's own maximized check (below)
        // sees the window's *current* state and skips forcing stale, small bounds onto it,
        // instead of a stale timer doing that later regardless of which path ran.
        ResolvePendingDpiCorrection(hwnd);

        if (IsZoomed(hwnd))
        {
            MoveMaximizedWindowToScreen(hwnd, target);
            return;
        }

        var (bounds, watchForSelfResize) = DetermineWindowBounds(hwnd, target);

        SetWindowPos(hwnd, IntPtr.Zero,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);

        if (watchForSelfResize)
            ScheduleDpiCompensationCheck(hwnd, bounds);
    }

    // Some per-monitor-DPI-aware apps resize themselves the moment they detect a DPI change,
    // regardless of what size they're handed - confirmed in ISSUES.md #3 (Chrome, Explorer do;
    // VLC, Steam don't, aside from Steam's own separate minimum-size limit). Rather than guess
    // a size that cancels out whatever an app might do, this always sets the plain correct
    // size directly, then reactively watches for exactly that: check whether the window is
    // still at the size it was given, and correct it back if an app changed it away. If it's
    // correct - the app self-corrected, or nothing needed correcting - this does nothing.
    //
    // Keyed by hwnd, not a flat list: confirmed bug (ISSUES.md #3) - moving the same window
    // again before its previous correction chain finished left the old chain running
    // unaware a newer move had superseded it, so it would later fire and yank the window back
    // to the earlier move's stale target, visibly "queuing up" jumps between screens. Starting
    // a new chain for a window now always resolves whatever chain is already running for it
    // first (see ResolvePendingDpiCorrection) rather than just cancelling it silently.
    private static readonly Dictionary<IntPtr, (System.Windows.Forms.Timer Timer, Rectangle FallbackBounds)> pendingDpiCorrections = new();

    // Also confirmed via ISSUES.md #3 (VLC): a second, subtler bug from the same root cause -
    // a brand new move reads the window's *current* size as its proportional baseline, and if
    // an earlier move's correction hadn't actually settled yet, that "current" size is a
    // transient, still-wrong value - so the error compounds across each quick move (e.g. an
    // extra 0.8x deflate bleeding through from an unsettled prior move). Stopping the old
    // timer alone doesn't fix this; the window itself must be forced to its known-correct
    // target *before* the new move reads its bounds, so every move always starts from a
    // settled, correct baseline.
    private static void ResolvePendingDpiCorrection(IntPtr hwnd)
    {
        if (!pendingDpiCorrections.TryGetValue(hwnd, out var pending)) return;

        pending.Timer.Stop();
        pending.Timer.Dispose();
        pendingDpiCorrections.Remove(hwnd);

        if (!GetWindowRect(hwnd, out RECT r)) return; // window gone
        if (IsZoomed(hwnd)) return; // maximized since the correction was scheduled - not ours to touch
        if (RoughlyEqual(ToRectangle(r), pending.FallbackBounds)) return; // already correct

        SetWindowPos(hwnd, IntPtr.Zero,
            pending.FallbackBounds.X, pending.FallbackBounds.Y,
            pending.FallbackBounds.Width, pending.FallbackBounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);
        DebugLog.Write($"DPI correction [{DescribeWindowProcess(hwnd)}]: resolved pending correction early to fallback={pending.FallbackBounds} before a new move");
    }

    // Only removes the dictionary entry if it still points at this exact timer - a timer that
    // was already superseded (and resolved) by ResolvePendingDpiCorrection must not remove a
    // newer chain's entry when its own (already-cancelled) Tick still fires.
    private static void UnregisterDpiCorrectionTimer(IntPtr hwnd, System.Windows.Forms.Timer timer)
    {
        if (pendingDpiCorrections.TryGetValue(hwnd, out var current) && current.Timer == timer)
            pendingDpiCorrections.Remove(hwnd);
    }

    // Most apps that self-correct do so almost immediately, well under a second - checking
    // fast first lets that common case resolve near-instantly instead of always paying the
    // full safe delay below. This check never forces anything; a miss just falls through to
    // the slower, already-proven-safe path.
    private const int FastCheckDelayMs = 150;

    private static void ScheduleDpiCompensationCheck(IntPtr hwnd, Rectangle fallbackBounds)
    {
        // Any prior pending correction for this window was already resolved at the top of
        // MoveWindowToScreen, before this move's own bounds were even calculated.
        var fastCheck = new System.Windows.Forms.Timer { Interval = FastCheckDelayMs };
        fastCheck.Tick += (s, e) =>
        {
            fastCheck.Stop();
            UnregisterDpiCorrectionTimer(hwnd, fastCheck);
            fastCheck.Dispose();

            string process = DescribeWindowProcess(hwnd);
            if (!GetWindowRect(hwnd, out RECT r)) { DebugLog.Write($"DPI correction [{process}]: window gone (fast check)"); return; }
            if (IsZoomed(hwnd)) { DebugLog.Write($"DPI correction [{process}]: now maximized, abandoning correction (fast check)"); return; }

            Rectangle actual = ToRectangle(r);
            if (RoughlyEqual(actual, fallbackBounds))
            {
                DebugLog.Write($"DPI correction [{process}]: actual={actual} already matches fallback={fallbackBounds} (fast check, {FastCheckDelayMs}ms)");
                return;
            }

            ScheduleDpiCorrectionRetries(hwnd, fallbackBounds);
        };
        pendingDpiCorrections[hwnd] = (fastCheck, fallbackBounds);
        fastCheck.Start();
    }

    // The delay can't be "correct" - there's no way to know a given app's own correction time
    // in advance, so this is a race no fixed delay fully eliminates, only makes unlikely. It's
    // a deliberately one-sided bet: a genuinely non-cooperating app (VLC/Steam) was already
    // stuck wrong indefinitely before this existed, so waiting longer before fixing it costs
    // nothing; a cooperating app (Chrome/Explorer) just needs the delay to reliably outlast its
    // own correction. 400ms was confirmed too short - it raced ahead of Chrome's own correction
    // and compounded into a wrong result (this method's fallback landing, then Chrome's own
    // shrink applying again on top of it). 1500ms leaves much more margin, confirmed safe.
    //
    // Bounded retry count - see ISSUES.md #3 (Steam). A few retries gives real margin against
    // timing variance without retrying forever against a window that's genuinely never going
    // to reach the target (e.g. Steam's own enforced minimum size).
    private const int MaxDpiCorrectionAttempts = 3;

    private static void ScheduleDpiCorrectionRetries(IntPtr hwnd, Rectangle fallbackBounds, int attemptsLeft = MaxDpiCorrectionAttempts)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            UnregisterDpiCorrectionTimer(hwnd, timer);
            timer.Dispose();

            string process = DescribeWindowProcess(hwnd);
            if (!GetWindowRect(hwnd, out RECT r)) { DebugLog.Write($"DPI correction [{process}]: window gone"); return; }
            if (IsZoomed(hwnd)) { DebugLog.Write($"DPI correction [{process}]: now maximized, abandoning correction"); return; }

            // Check against the CORRECT target (fallbackBounds), not against what this method
            // originally set - checking whether the window actually reached the right answer,
            // rather than whether it merely changed at all, isn't fooled by an app doing some
            // small unrelated adjustment of its own (confirmed: Steam drifting 63px on its own,
            // nowhere near the real target, which used to pass a "did anything change" check).
            Rectangle actual = ToRectangle(r);
            if (RoughlyEqual(actual, fallbackBounds))
            {
                DebugLog.Write($"DPI correction [{process}]: actual={actual} already matches fallback={fallbackBounds}, attempt {MaxDpiCorrectionAttempts - attemptsLeft + 1}");
                return;
            }

            bool applied = SetWindowPos(hwnd, IntPtr.Zero,
                fallbackBounds.X, fallbackBounds.Y, fallbackBounds.Width, fallbackBounds.Height,
                SWP_NOZORDER | SWP_NOACTIVATE);
            DebugLog.Write($"DPI correction [{process}]: actual={actual} -> fallback={fallbackBounds}, applied={applied}, attemptsLeft={attemptsLeft}");

            if (attemptsLeft > 1)
                ScheduleDpiCorrectionRetries(hwnd, fallbackBounds, attemptsLeft - 1);
        };
        pendingDpiCorrections[hwnd] = (timer, fallbackBounds);
        timer.Start();
    }

    // Windows' own invisible resize-border margins are DPI-dependent and can shift by a few
    // pixels across a DPI-crossing move independent of the app - not the hundreds-of-pixels
    // scale of a real DPI self-correction. 8px comfortably clears that OS-level noise while
    // staying far below any genuine app resize.
    private const int DpiCorrectionToleranceInPixels = 8;

    private static bool RoughlyEqual(Rectangle a, Rectangle b) =>
        Math.Abs(a.X - b.X) <= DpiCorrectionToleranceInPixels &&
        Math.Abs(a.Y - b.Y) <= DpiCorrectionToleranceInPixels &&
        Math.Abs(a.Width - b.Width) <= DpiCorrectionToleranceInPixels &&
        Math.Abs(a.Height - b.Height) <= DpiCorrectionToleranceInPixels;

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
        var bounds = WindowPlacement.Centered(target.Bounds, new Size(WindowWidth, WindowHeight));

        ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, IntPtr.Zero,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);
        ShowWindow(hwnd, SW_MAXIMIZE);
    }

    // Where and what size a non-maximized window should become: it lands at the same
    // relative position it had on its source monitor, at the same percentage of screen size
    // it had before, so it neither jumps to an unrelated spot nor looks tiny/oversized
    // crossing between differently sized monitors - see WindowPlacement.ProportionalPosition
    // and ProportionalSize. Falls back to a fixed size centered on the target monitor when
    // the window's current bounds or monitor can't be determined, same as the app has
    // always done.
    //
    // Also returns WatchForSelfResize: true only when this move crosses a real DPI boundary on
    // a per-monitor-DPI-aware window - the only case where the target app might change this
    // size on its own afterward (Chrome/Explorer auto-resize themselves the moment they detect
    // a DPI change; VLC/Steam don't). When true, MoveWindowToScreen schedules a reactive check
    // that corrects the window back to Bounds if anything changes it away - see
    // ScheduleDpiCompensationCheck and ISSUES.md's resolved DPI-sizing history for why this
    // replaced an earlier approach that tried to pre-guess a DPI-compensated size instead.
    private static (Rectangle Bounds, bool WatchForSelfResize) DetermineWindowBounds(IntPtr hwnd, Screen target)
    {
        var fallbackSize = new Size(WindowWidth, WindowHeight);

        if (!GetWindowRect(hwnd, out RECT r)) return (WindowPlacement.Centered(target.WorkingArea, fallbackSize), false);

        Rectangle currentBounds = ToRectangle(r);
        Screen[] screens = Screen.AllScreens;
        var layout = new MonitorLayout(Array.ConvertAll(screens, s => s.WorkingArea));
        int sourceIndex = layout.IndexOfMonitorShowing(currentBounds);
        if (sourceIndex < 0) return (WindowPlacement.Centered(target.WorkingArea, fallbackSize), false); // no monitors at all

        Rectangle sourceWorkingArea = layout[sourceIndex];

        // The actual sizing decision (proportional size, clamped to the target monitor) is
        // WindowPlacement.DetermineTargetSize, in WindowMover.Core where it can be unit tested.
        Size size = WindowPlacement.DetermineTargetSize(sourceWorkingArea, currentBounds.Size, target.WorkingArea);
        Rectangle bounds = WindowPlacement.ProportionalPosition(sourceWorkingArea, currentBounds, target.WorkingArea, size);

        bool watchForSelfResize = CrossesRealDpiBoundary(hwnd, target.WorkingArea);
        return (bounds, watchForSelfResize);
    }

    // Is the target window per-monitor-DPI-aware (only those get WM_DPICHANGED and might
    // resize themselves in response), and does this move actually cross a DPI boundary?
    // Returns false - don't bother watching - if either can't be determined.
    private static bool CrossesRealDpiBoundary(IntPtr hwnd, Rectangle targetMonitorBounds)
    {
        int awareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd));
        if (awareness != DPI_AWARENESS_PER_MONITOR_AWARE) return false;

        // Read the source monitor before the window moves - MonitorFromWindow still
        // reflects where it currently is at this point in the call chain.
        IntPtr sourceMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        RECT targetRect = ToRect(targetMonitorBounds);
        IntPtr targetMonitor = MonitorFromRect(ref targetRect, MONITOR_DEFAULTTONEAREST);

        if (GetDpiForMonitor(sourceMonitor, MDT_EFFECTIVE_DPI, out uint sX, out uint sY) != 0) return false;
        if (GetDpiForMonitor(targetMonitor, MDT_EFFECTIVE_DPI, out uint tX, out uint tY) != 0) return false;

        return sX != tX || sY != tY;
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