using static NativeMethods;

// Some per-monitor-DPI-aware apps resize themselves the moment they detect a DPI change,
// regardless of what size they're handed - confirmed in ISSUES.md #3 (Chrome, Explorer do;
// VLC, Steam don't, aside from Steam's own separate minimum-size limit). Rather than guess
// a size that cancels out whatever an app might do, WindowMoveActions always sets the plain
// correct size directly, then this class reactively watches for exactly that: check whether
// the window is still at the size it was given, and correct it back if an app changed it
// away. If it's correct - the app self-corrected, or nothing needed correcting - this does
// nothing.
internal static class DpiCorrectionScheduler
{
    // Watches for the user grabbing a window's own move/resize border while a correction is
    // pending for it. EVENT_SYSTEM_MOVESIZESTART only fires for that interactive drag loop -
    // a program calling SetWindowPos (an app self-correcting, or this class's own corrections)
    // never triggers it - so it cleanly tells "the user is now in control of this window"
    // apart from "the app resized itself" without guessing from position/size alone. Confirmed
    // needed: without this, a pending correction would fight a manual resize mid-drag, snapping
    // the window back to the DPI-move's target size after the user had already changed it.
    private static IntPtr manualResizeHookId;

    // SetWinEventHook only keeps the delegate alive via the unmanaged callback pointer, not a
    // managed reference - same GC-collection hazard as MouseHook's hookProc, same fix.
    private static WinEventDelegate? manualResizeHookProc;

    public static void InstallManualResizeWatcher()
    {
        manualResizeHookProc = OnManualMoveOrResizeStart;
        manualResizeHookId = SetWinEventHook(
            EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZESTART,
            IntPtr.Zero, manualResizeHookProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public static void UninstallManualResizeWatcher()
    {
        if (manualResizeHookId != IntPtr.Zero) UnhookWinEvent(manualResizeHookId);
    }

    private static void OnManualMoveOrResizeStart(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // idObject identifies which part of the window started moving/sizing - OBJID_WINDOW
        // means the window itself, as opposed to some child control reporting its own event.
        if (idObject != OBJID_WINDOW) return;
        if (!pendingDpiCorrections.TryGetValue(hwnd, out var pending)) return;

        RemovePending(hwnd, pending);
        DebugLog.Write($"DPI correction [{DebugLog.DescribeWindowProcess(hwnd)}]: abandoned - user began an interactive move/resize");
    }

    private sealed class PendingCorrection
    {
        public required System.Windows.Forms.Timer DebounceTimer;
        public required Rectangle FallbackBounds;
        public required DateTime Deadline;
        // Whether WindowMoveActions hid this window for the duration of the correction, and
        // therefore whether revealing it also has to put it back in front - hiding the
        // foreground window hands the foreground to whatever was behind it, and SW_SHOWNA
        // deliberately doesn't take it back.
        public required bool BringToFrontOnReveal;
    }

    // Keyed by hwnd, not a flat list: confirmed bug (ISSUES.md #3) - moving the same window
    // again before its previous correction chain finished left the old chain running
    // unaware a newer move had superseded it, so it would later fire and yank the window back
    // to the earlier move's stale target, visibly "queuing up" jumps between screens. Starting
    // a new chain for a window now always resolves whatever chain is already running for it
    // first (see ResolvePendingDpiCorrection) rather than just cancelling it silently.
    private static readonly Dictionary<IntPtr, PendingCorrection> pendingDpiCorrections = new();

    // How long a window's bounds must sit unchanged (no EVENT_OBJECT_LOCATIONCHANGE for it)
    // before a wrong size is treated as final rather than still in motion. This only needs to
    // debounce a single resize's own internal churn, not outlast an app's entire correction
    // the way the old fixed-poll design had to guess at - the event tells us the instant
    // something actually happens, so we only ever wait as long as real activity demands.
    private const int DebounceMs = 100;

    // Hard cap on total time spent watching a window, regardless of how many times the
    // debounce has been restarted by fresh events or re-armed after a forced correction -
    // guards the same case the old bounded-retry-count guarded (ISSUES.md #3, Steam): a
    // window that keeps drifting and never truly settles on the right answer.
    private const int MaxTotalCorrectionWindowMs = 5000;

    // Windows' own invisible resize-border margins are DPI-dependent and can shift by a few
    // pixels across a DPI-crossing move independent of the app - not the hundreds-of-pixels
    // scale of a real DPI self-correction. 8px comfortably clears that OS-level noise while
    // staying far below any genuine app resize.
    private const int DpiCorrectionToleranceInPixels = 8;

    // Fires on any window's bounds actually changing, system-wide - installed only while at
    // least one correction is pending (see EnsureLocationChangeWatcherInstalled/RemovePending)
    // rather than for the app's whole lifetime, since this event is far chattier than
    // EVENT_SYSTEM_MOVESIZESTART and would otherwise run against the "lightweight" goal.
    private static IntPtr locationChangeHookId;
    private static WinEventDelegate? locationChangeHookProc;

    // Also confirmed via ISSUES.md #3 (VLC): a second, subtler bug from the same root cause -
    // a brand new move reads the window's *current* size as its proportional baseline, and if
    // an earlier move's correction hadn't actually settled yet, that "current" size is a
    // transient, still-wrong value - so the error compounds across each quick move (e.g. an
    // extra 0.8x deflate bleeding through from an unsettled prior move). Stopping the old
    // timer alone doesn't fix this; the window itself must be forced to its known-correct
    // target *before* the new move reads its bounds, so every move always starts from a
    // settled, correct baseline.
    public static void ResolvePendingDpiCorrection(IntPtr hwnd)
    {
        if (!pendingDpiCorrections.TryGetValue(hwnd, out var pending)) return;
        RemovePending(hwnd, pending);

        if (!GetWindowRect(hwnd, out RECT r)) return; // window gone
        if (IsZoomed(hwnd)) return; // maximized since the correction was scheduled - not ours to touch
        if (RoughlyEqual(ToRectangle(r), pending.FallbackBounds)) return; // already correct

        SetWindowPos(hwnd, IntPtr.Zero,
            pending.FallbackBounds.X, pending.FallbackBounds.Y,
            pending.FallbackBounds.Width, pending.FallbackBounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);
        DebugLog.Write($"DPI correction [{DebugLog.DescribeWindowProcess(hwnd)}]: resolved pending correction early to fallback={pending.FallbackBounds} before a new move");
    }

    public static void ScheduleDpiCompensationCheck(IntPtr hwnd, Rectangle fallbackBounds, bool bringToFrontOnReveal)
    {
        // Any prior pending correction for this window was already resolved at the top of
        // MoveWindowToScreen, before this move's own bounds were even calculated.
        EnsureLocationChangeWatcherInstalled();

        var pending = new PendingCorrection
        {
            DebounceTimer = null!,
            FallbackBounds = fallbackBounds,
            Deadline = DateTime.UtcNow.AddMilliseconds(MaxTotalCorrectionWindowMs),
            BringToFrontOnReveal = bringToFrontOnReveal
        };
        pending.DebounceTimer = StartDebounceTimer(hwnd);
        pendingDpiCorrections[hwnd] = pending;
    }

    // A location-change event means this window's bounds just changed, for any reason - reset
    // the debounce so OnSettled only fires once things have actually gone quiet, instead of
    // judging a still-moving window "final" mid-motion.
    private static void OnLocationChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != OBJID_WINDOW) return;
        if (!pendingDpiCorrections.TryGetValue(hwnd, out var pending)) return;

        pending.DebounceTimer.Stop();
        pending.DebounceTimer.Dispose();
        pending.DebounceTimer = StartDebounceTimer(hwnd);
    }

    private static System.Windows.Forms.Timer StartDebounceTimer(IntPtr hwnd)
    {
        var timer = new System.Windows.Forms.Timer { Interval = DebounceMs };
        timer.Tick += (s, e) =>
        {
            // A WinForms Timer keeps ticking on its own interval forever once started,
            // whether or not anything still references it - must stop and dispose THIS
            // instance here, before OnSettled replaces pending.DebounceTimer with a new one,
            // or the old one leaks and keeps firing OnSettled indefinitely in the background.
            timer.Stop();
            timer.Dispose();
            OnSettled(hwnd);
        };
        timer.Start();
        return timer;
    }

    // Fires once a window's bounds have sat unchanged for DebounceMs - whether that's because
    // nothing ever needed fixing, an app finished self-correcting, or a self-resize landed
    // wrong and then just stopped there (the common case observed live: Explorer scaling to
    // its old size times the DPI ratio, immediately, then never touching it again).
    private static void OnSettled(IntPtr hwnd)
    {
        if (!pendingDpiCorrections.TryGetValue(hwnd, out var pending)) return;

        string process = DebugLog.DescribeWindowProcess(hwnd);
        if (!GetWindowRect(hwnd, out RECT r)) { DebugLog.Write($"DPI correction [{process}]: window gone"); RemovePending(hwnd, pending); return; }
        if (IsZoomed(hwnd)) { DebugLog.Write($"DPI correction [{process}]: now maximized, abandoning correction"); RemovePending(hwnd, pending); return; }

        Rectangle actual = ToRectangle(r);
        if (RoughlyEqual(actual, pending.FallbackBounds))
        {
            DebugLog.Write($"DPI correction [{process}]: actual={actual} already matches fallback={pending.FallbackBounds} (settled)");
            RemovePending(hwnd, pending);
            return;
        }

        if (DateTime.UtcNow >= pending.Deadline)
        {
            bool giveUpApplied = SetWindowPos(hwnd, IntPtr.Zero,
                pending.FallbackBounds.X, pending.FallbackBounds.Y, pending.FallbackBounds.Width, pending.FallbackBounds.Height,
                SWP_NOZORDER | SWP_NOACTIVATE);
            DebugLog.Write($"DPI correction [{process}]: actual={actual} hit max correction window -> fallback={pending.FallbackBounds}, applied={giveUpApplied}, giving up");
            RemovePending(hwnd, pending);
            return;
        }

        bool applied = SetWindowPos(hwnd, IntPtr.Zero,
            pending.FallbackBounds.X, pending.FallbackBounds.Y, pending.FallbackBounds.Width, pending.FallbackBounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);
        DebugLog.Write($"DPI correction [{process}]: actual={actual} settled wrong -> fallback={pending.FallbackBounds}, applied={applied}");

        // Re-arm rather than remove: the correction just applied might get fought again (or
        // might not stick for some other reason), and the deadline above bounds how long this
        // can keep happening.
        pending.DebounceTimer = StartDebounceTimer(hwnd);
    }

    // The single choke point every correction lifecycle ends through - settled correctly, hit
    // the deadline, abandoned for a manual resize, or superseded by a new move - so it's also
    // the one safe place to un-hide the window WindowMoveActions hid before starting: whatever
    // ended the correction, the window is guaranteed to come back. SW_SHOWNA on an
    // already-visible window (the common case that was never hidden) is a harmless no-op.
    private static void RemovePending(IntPtr hwnd, PendingCorrection pending)
    {
        pending.DebounceTimer.Stop();
        pending.DebounceTimer.Dispose();
        pendingDpiCorrections.Remove(hwnd);
        if (pendingDpiCorrections.Count == 0) UninstallLocationChangeWatcher();
        ShowWindow(hwnd, SW_SHOWNA);
        // Only for a window this app actually hid: anything else here is already wherever the
        // user left it, and yanking it forward would be a change nobody asked for.
        if (pending.BringToFrontOnReveal) WindowMoveActions.BringToFront(hwnd);
    }

    private static void EnsureLocationChangeWatcherInstalled()
    {
        if (locationChangeHookId != IntPtr.Zero) return;
        locationChangeHookProc = OnLocationChanged;
        locationChangeHookId = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, locationChangeHookProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private static void UninstallLocationChangeWatcher()
    {
        if (locationChangeHookId == IntPtr.Zero) return;
        UnhookWinEvent(locationChangeHookId);
        locationChangeHookId = IntPtr.Zero;
        locationChangeHookProc = null;
    }

    // Called on app shutdown so no timer callback or hook can fire after Application.Run()
    // has returned.
    public static void Shutdown()
    {
        foreach (var hwnd in new List<IntPtr>(pendingDpiCorrections.Keys))
            RemovePending(hwnd, pendingDpiCorrections[hwnd]);
    }

    private static bool RoughlyEqual(Rectangle a, Rectangle b) =>
        Math.Abs(a.X - b.X) <= DpiCorrectionToleranceInPixels &&
        Math.Abs(a.Y - b.Y) <= DpiCorrectionToleranceInPixels &&
        Math.Abs(a.Width - b.Width) <= DpiCorrectionToleranceInPixels &&
        Math.Abs(a.Height - b.Height) <= DpiCorrectionToleranceInPixels;
}
