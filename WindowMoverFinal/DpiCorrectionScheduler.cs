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

        pending.Timer.Stop();
        pending.Timer.Dispose();
        pendingDpiCorrections.Remove(hwnd);
        DebugLog.Write($"DPI correction [{DebugLog.DescribeWindowProcess(hwnd)}]: abandoned - user began an interactive move/resize");
    }

    // Keyed by hwnd, not a flat list: confirmed bug (ISSUES.md #3) - moving the same window
    // again before its previous correction chain finished left the old chain running
    // unaware a newer move had superseded it, so it would later fire and yank the window back
    // to the earlier move's stale target, visibly "queuing up" jumps between screens. Starting
    // a new chain for a window now always resolves whatever chain is already running for it
    // first (see ResolvePendingDpiCorrection) rather than just cancelling it silently.
    private static readonly Dictionary<IntPtr, (System.Windows.Forms.Timer Timer, Rectangle FallbackBounds)> pendingDpiCorrections = new();

    // Interval between the read-only checks in ScheduleCheck, below.
    private const int FastCheckDelayMs = 150;

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

    // Windows' own invisible resize-border margins are DPI-dependent and can shift by a few
    // pixels across a DPI-crossing move independent of the app - not the hundreds-of-pixels
    // scale of a real DPI self-correction. 8px comfortably clears that OS-level noise while
    // staying far below any genuine app resize.
    private const int DpiCorrectionToleranceInPixels = 8;

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
        DebugLog.Write($"DPI correction [{DebugLog.DescribeWindowProcess(hwnd)}]: resolved pending correction early to fallback={pending.FallbackBounds} before a new move");
    }

    // Only removes the dictionary entry if it still points at this exact timer - a timer that
    // was already superseded (and resolved) by ResolvePendingDpiCorrection must not remove a
    // newer chain's entry when its own (already-cancelled) Tick still fires.
    private static void UnregisterDpiCorrectionTimer(IntPtr hwnd, System.Windows.Forms.Timer timer)
    {
        if (pendingDpiCorrections.TryGetValue(hwnd, out var current) && current.Timer == timer)
            pendingDpiCorrections.Remove(hwnd);
    }

    public static void ScheduleDpiCompensationCheck(IntPtr hwnd, Rectangle fallbackBounds)
    {
        // Any prior pending correction for this window was already resolved at the top of
        // MoveWindowToScreen, before this move's own bounds were even calculated.
        ScheduleCheck(hwnd, fallbackBounds, previousActual: null);
    }

    // Confirmed via windowmover-debug.log on a real DPI-crossing move: a self-resizing app's
    // mistake is often already final by the very first check and then just sits there -
    // Explorer landed on exactly its old size times the monitor's DPI ratio, immediately, and
    // never moved again across five checks spanning 750ms. Waiting out the full 1500ms retry
    // cadence to fix a value that's already stable adds visible delay for nothing - that
    // cadence exists to avoid racing an app that's still actively correcting itself, not one
    // that's already finished (however wrongly). So: check twice, a beat apart. If the wrong
    // size is identical both times, nothing is left to race - force it immediately. Only when
    // it's still visibly changing between checks (genuine risk of racing an in-progress
    // correction) does control pass to the slower, proven-safe retry chain.
    private static void ScheduleCheck(IntPtr hwnd, Rectangle fallbackBounds, Rectangle? previousActual)
    {
        var timer = new System.Windows.Forms.Timer { Interval = FastCheckDelayMs };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            UnregisterDpiCorrectionTimer(hwnd, timer);
            timer.Dispose();

            string process = DebugLog.DescribeWindowProcess(hwnd);
            if (!GetWindowRect(hwnd, out RECT r)) { DebugLog.Write($"DPI correction [{process}]: window gone (fast check)"); return; }
            if (IsZoomed(hwnd)) { DebugLog.Write($"DPI correction [{process}]: now maximized, abandoning correction (fast check)"); return; }

            Rectangle actual = ToRectangle(r);
            if (RoughlyEqual(actual, fallbackBounds))
            {
                DebugLog.Write($"DPI correction [{process}]: actual={actual} already matches fallback={fallbackBounds} (fast check)");
                return;
            }

            if (previousActual is Rectangle prev && RoughlyEqual(actual, prev))
            {
                bool applied = SetWindowPos(hwnd, IntPtr.Zero,
                    fallbackBounds.X, fallbackBounds.Y, fallbackBounds.Width, fallbackBounds.Height,
                    SWP_NOZORDER | SWP_NOACTIVATE);
                DebugLog.Write($"DPI correction [{process}]: actual={actual} stable across two checks -> fallback={fallbackBounds}, applied={applied} (fast path)");
                return;
            }

            if (previousActual is null)
            {
                DebugLog.Write($"DPI correction [{process}]: actual={actual} off fallback={fallbackBounds} (first check) - checking again to see if it's still moving");
                ScheduleCheck(hwnd, fallbackBounds, actual);
            }
            else
            {
                DebugLog.Write($"DPI correction [{process}]: actual={actual} still changing (was {previousActual}) - falling to slow retry cadence");
                ScheduleDpiCorrectionRetries(hwnd, fallbackBounds);
            }
        };
        pendingDpiCorrections[hwnd] = (timer, fallbackBounds);
        timer.Start();
    }

    private static void ScheduleDpiCorrectionRetries(IntPtr hwnd, Rectangle fallbackBounds, int attemptsLeft = MaxDpiCorrectionAttempts)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            UnregisterDpiCorrectionTimer(hwnd, timer);
            timer.Dispose();

            string process = DebugLog.DescribeWindowProcess(hwnd);
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

    private static bool RoughlyEqual(Rectangle a, Rectangle b) =>
        Math.Abs(a.X - b.X) <= DpiCorrectionToleranceInPixels &&
        Math.Abs(a.Y - b.Y) <= DpiCorrectionToleranceInPixels &&
        Math.Abs(a.Width - b.Width) <= DpiCorrectionToleranceInPixels &&
        Math.Abs(a.Height - b.Height) <= DpiCorrectionToleranceInPixels;
}
