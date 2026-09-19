using static NativeMethods;

// What happens to a window after WindowMove has moved it, until it stops changing.
//
// Two different moves need that, for two different reasons:
//
//   - A DPI-crossing move. Some per-monitor-DPI-aware apps resize themselves the moment they
//     detect a DPI change, regardless of what size they are handed - confirmed in ISSUES.md #3
//     (Chrome, Explorer do; VLC, Steam don't, aside from Steam's own separate minimum-size
//     limit). Rather than guess a size that cancels out whatever an app might do, WindowMove
//     always sets the plain correct size and this watches for exactly that: if the window is
//     no longer at the size it was given, put it back. If it is - the app self-corrected, or
//     nothing needed correcting - this does nothing.
//   - A maximized move. Nothing about its size to correct; it just needs a moment to stop
//     moving before it is revealed again, because restoring, repositioning and re-maximizing
//     are three animations deep.
//
// Both are the same watch with a different answer to "is there a size to keep?", which is
// what PendingWatch.SizeToKeep being null means. Either way this is the one place a window
// hidden for a move is revealed - see EndWatch.
internal static class MoveSettleWatcher
{
    // Watches for the user grabbing a window's own move/resize border while a watch is
    // pending for it. EVENT_SYSTEM_MOVESIZESTART only fires for that interactive drag loop -
    // a program calling SetWindowPos (an app self-correcting, or this class's own corrections)
    // never triggers it - so it cleanly tells "the user is now in control of this window"
    // apart from "the app resized itself" without guessing from position/size alone. Confirmed
    // needed: without this, a pending correction would fight a manual resize mid-drag, snapping
    // the window back to the DPI-move's target size after the user had already changed it.
    private static IntPtr manualResizeHookId;

    // SetWinEventHook only keeps the delegate alive via the unmanaged callback pointer, not a
    // managed reference - same GC-collection hazard as MouseGestureHook's hookProc, same fix.
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
        if (!pendingWatches.TryGetValue(hwnd, out var pending)) return;

        EndWatch(hwnd, pending);
        DebugLog.Write($"DPI correction [{DebugLog.DescribeWindowProcess(hwnd)}]: abandoned - user began an interactive move/resize");
    }

    private sealed class PendingWatch
    {
        public required System.Windows.Forms.Timer DebounceTimer;
        // The size to force back if an app resizes itself away from it, or null for a watch
        // that exists only to wait until the window stops changing - which is what a maximized
        // move needs.
        public required Rectangle? SizeToKeep;
        public required DateTime Deadline;
        // How to undo the transparency WindowMove applied for the duration of the move, or
        // null when it did not hide this window at all.
        public required TransparencyRestore? Hidden;
    }

    // Keyed by hwnd, not a flat list: confirmed bug (ISSUES.md #3) - moving the same window
    // again before its previous correction chain finished left the old chain running
    // unaware a newer move had superseded it, so it would later fire and yank the window back
    // to the earlier move's stale target, visibly "queuing up" jumps between screens. Starting
    // a new watch for a window now always finishes whatever watch is already running for it
    // first (see FinishPendingWatch) rather than just cancelling it silently.
    private static readonly Dictionary<IntPtr, PendingWatch> pendingWatches = new();

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
    private const int MaxTotalWatchMs = 5000;

    // Windows' own invisible resize-border margins are DPI-dependent and can shift by a few
    // pixels across a DPI-crossing move independent of the app - not the hundreds-of-pixels
    // scale of a real DPI self-correction. 8px comfortably clears that OS-level noise while
    // staying far below any genuine app resize.
    private const int SizeToleranceInPixels = 8;

    // Fires on any window's bounds actually changing, system-wide - installed only while at
    // least one watch is pending (see EnsureLocationChangeWatcherInstalled/EndWatch) rather
    // than for the app's whole lifetime, since this event is far chattier than
    // EVENT_SYSTEM_MOVESIZESTART and would otherwise run against the "lightweight" goal.
    private static IntPtr locationChangeHookId;
    private static WinEventDelegate? locationChangeHookProc;

    // Watches a window that must keep the size it was just given, correcting it back if the
    // app changes it away. Any prior pending watch for this window was already finished at the
    // top of WindowMove.ToMonitor, before this move's own bounds were even calculated.
    public static void KeepSizeUntilSettled(IntPtr hwnd, Rectangle sizeToKeep, TransparencyRestore? hidden) =>
        StartWatch(hwnd, sizeToKeep, hidden);

    // Keeps a hidden window hidden until it stops moving, then reveals it - no size to police,
    // so the first quiet moment ends it. A maximized move visibly restores the window, drops it
    // on the other monitor and maximizes it again, and Windows animates each of those steps;
    // watching for the end of that is what turns it from a stagger into an arrival.
    public static void RevealWhenSettled(IntPtr hwnd, TransparencyRestore hidden) =>
        StartWatch(hwnd, sizeToKeep: null, hidden);

    private static void StartWatch(IntPtr hwnd, Rectangle? sizeToKeep, TransparencyRestore? hidden)
    {
        EnsureLocationChangeWatcherInstalled();

        pendingWatches[hwnd] = new PendingWatch
        {
            DebounceTimer = StartDebounceTimer(hwnd),
            SizeToKeep = sizeToKeep,
            Deadline = DateTime.UtcNow.AddMilliseconds(MaxTotalWatchMs),
            Hidden = hidden
        };
    }

    // Also confirmed via ISSUES.md #3 (VLC): a second, subtler bug from the same root cause -
    // a brand new move reads the window's *current* size as its proportional baseline, and if
    // an earlier move's correction hadn't actually settled yet, that "current" size is a
    // transient, still-wrong value - so the error compounds across each quick move (e.g. an
    // extra 0.8x deflate bleeding through from an unsettled prior move). Stopping the old
    // timer alone doesn't fix this; the window itself must be forced to its known-correct
    // target *before* the new move reads its bounds, so every move always starts from a
    // settled, correct baseline.
    public static void FinishPendingWatch(IntPtr hwnd)
    {
        if (!pendingWatches.TryGetValue(hwnd, out var pending)) return;
        EndWatch(hwnd, pending);

        if (pending.SizeToKeep is not { } wanted) return; // nothing to correct, only to reveal
        if (!GetWindowRect(hwnd, out RECT r)) return; // window gone
        if (IsZoomed(hwnd)) return; // maximized since the watch started - not ours to touch
        if (RoughlyEqual(ToRectangle(r), wanted)) return; // already correct

        ForceBounds(hwnd, wanted);
        DebugLog.Write($"DPI correction [{DebugLog.DescribeWindowProcess(hwnd)}]: resolved pending correction early to fallback={wanted} before a new move");
    }

    // A location-change event means this window's bounds just changed, for any reason - reset
    // the debounce so OnSettled only fires once things have actually gone quiet, instead of
    // judging a still-moving window "final" mid-motion.
    private static void OnLocationChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != OBJID_WINDOW) return;
        if (!pendingWatches.TryGetValue(hwnd, out var pending)) return;

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
        if (!pendingWatches.TryGetValue(hwnd, out var pending)) return;

        string process = DebugLog.DescribeWindowProcess(hwnd);
        if (!GetWindowRect(hwnd, out RECT r)) { DebugLog.Write($"DPI correction [{process}]: window gone"); EndWatch(hwnd, pending); return; }
        if (IsZoomed(hwnd)) { DebugLog.Write($"DPI correction [{process}]: now maximized, abandoning correction"); EndWatch(hwnd, pending); return; }

        if (pending.SizeToKeep is not { } wanted)
        {
            DebugLog.Write($"Move [{process}]: window settled after a maximized move, revealing");
            EndWatch(hwnd, pending);
            return;
        }

        Rectangle actual = ToRectangle(r);
        if (RoughlyEqual(actual, wanted))
        {
            DebugLog.Write($"DPI correction [{process}]: actual={actual} already matches fallback={wanted} (settled)");
            EndWatch(hwnd, pending);
            return;
        }

        bool outOfTime = DateTime.UtcNow >= pending.Deadline;
        bool applied = ForceBounds(hwnd, wanted);
        DebugLog.Write(outOfTime
            ? $"DPI correction [{process}]: actual={actual} hit max correction window -> fallback={wanted}, applied={applied}, giving up"
            : $"DPI correction [{process}]: actual={actual} settled wrong -> fallback={wanted}, applied={applied}");

        if (outOfTime) { EndWatch(hwnd, pending); return; }

        // Re-arm rather than end: the correction just applied might get fought again (or might
        // not stick for some other reason), and the deadline above bounds how long this can
        // keep happening.
        pending.DebounceTimer = StartDebounceTimer(hwnd);
    }

    private static bool ForceBounds(IntPtr hwnd, Rectangle bounds) =>
        SetWindowPos(hwnd, IntPtr.Zero,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);

    // The single choke point every watch ends through - settled correctly, hit the deadline,
    // abandoned for a manual resize, or superseded by a new move - so it's also the one safe
    // place to undo the transparency WindowMove applied before starting: whatever ended the
    // watch, the window is guaranteed to come back. Nothing is done to a window this app never
    // hid; it is already exactly as the user left it, and both revealing and raising it would
    // be changes nobody asked for.
    private static void EndWatch(IntPtr hwnd, PendingWatch pending)
    {
        pending.DebounceTimer.Stop();
        pending.DebounceTimer.Dispose();
        pendingWatches.Remove(hwnd);
        if (pendingWatches.Count == 0) UninstallLocationChangeWatcher();

        if (pending.Hidden is not { } restore) return;
        WindowTransparency.Reveal(hwnd, restore);
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
        foreach (var hwnd in new List<IntPtr>(pendingWatches.Keys))
            EndWatch(hwnd, pendingWatches[hwnd]);
    }

    private static bool RoughlyEqual(Rectangle a, Rectangle b) =>
        Math.Abs(a.X - b.X) <= SizeToleranceInPixels &&
        Math.Abs(a.Y - b.Y) <= SizeToleranceInPixels &&
        Math.Abs(a.Width - b.Width) <= SizeToleranceInPixels &&
        Math.Abs(a.Height - b.Height) <= SizeToleranceInPixels;
}
