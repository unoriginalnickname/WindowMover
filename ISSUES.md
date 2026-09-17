# Known issues and investigation history

Working notes on open problems with WindowMover, kept so we stop re-deriving the
same facts. Update this file as issues get resolved or new facts are found -
don't let it go stale. Resolved issues move to "Past issues" below, in full,
rather than being deleted - the history is what stops us re-deriving facts.

# Current issues

## 1. Non-maximized window moves flicker more than a plain drag - OPEN, PARKED

**Symptom:** moving a normal (non-maximized) window with WindowMover visibly
flickers/redraws noticeably more than just dragging the same window with the
mouse. Not yet investigated in depth. Concrete repro: a browser playing a
YouTube video - the video/page content visibly flickers on a WindowMover
move, but not when the same window is dragged by hand.

**Retracted claim:** this entry originally said a plain drag "only ever
changes position" and that DWM composites that as a cheap transform with no
repaint, no resize involved. That's wrong and wasn't verified before being
written down - user pointed out a plain drag between the two monitors keeps a
window's size close to correct on *both* of them, which it couldn't do
without resizing. A per-monitor-DPI-aware app gets `WM_DPICHANGED` (with a
suggested new rect) as it crosses the monitor boundary mid-drag and resizes
itself right then, live - the resize is real, not skipped.

**Second retracted claim:** the next guess here was that the live drag's
resize lands incrementally, frame-by-frame, versus `MoveWindowToScreen`'s one
instantaneous jump - also wrong, also written down before checking. User
observed what actually happens on a plain drag: the window gets replaced by a
plain gray outline for the duration of the drag, that outline is what crosses
onto the other monitor, and the real window (with its real content) only
reappears once, already at its final size and position, when the outline
settles. That's "Show window contents while dragging" off, not a live-content
interactive resize at all.

**Outline-masking hypothesis ruled out by direct test:** user turned "Show
window contents while dragging" ON and repeated the drag with real content
visible throughout. The resize at the DPI-crossing threshold was still
smooth, no flicker at all. So it isn't about a placeholder hiding the
transition - the eye is watching real content the whole time either way, and
only WindowMover's version flickers.

**Current hypothesis, not yet confirmed:** what's actually different is that
a live drag's resize happens *inside* an interactive move/size loop (the one
that starts at `WM_ENTERSIZEMOVE` and ends at `WM_EXITSIZEMOVE`), which DWM
gives special live-resize compositing treatment - confirmed smooth even with
content visible, per the test above. `MoveWindowToScreen` calls `SetWindowPos`
as a single one-shot resize+move with no such session around it, so DWM
treats it as an ordinary, uncomposited resize instead of a live one. If so,
the fix isn't about the resize's size or timing but about whether the target
window is put into (or made to look like it's in) that same interactive
session DWM already handles smoothly.

Microsoft's own `SetWindowPos` docs add one more real data point: unless
`SWP_NOCOPYBITS` is passed, "the valid contents of the client area are saved
and copied back into the client area after the window is sized or
repositioned" - i.e. the default behavior carries old pixels forward,
stretched/offset into the new size, until the app repaints for real.

**Tried:** added `SWP_NOCOPYBITS` to the `SetWindowPos` call in
`MoveWindowToScreen` (`WindowMoverFinal/Program.cs`), rebuilt Release, killed
the stale running instance and relaunched the new build (confirmed via
`Get-Process` start time), then repeated the YouTube-in-browser repro.
**Result: no improvement** - contents still visibly resize and shuffle around
within the moved window. Whatever the old-bits copy-back was contributing, it
wasn't the (or wasn't the whole) cause. Flag stays in the code since it's
harmless and arguably still correct to have, but it does not fix this issue by
itself.

**Tried:** bracketed the `SetWindowPos` call with synthetic
`WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` sent via `SendMessageTimeout` (bounded,
`SMTO_ABORTIFHUNG`, so a hung target can't stall the mouse hook thread the
way ISSUES.md #2 documents) - rebuilt Release, killed the stale instance,
relaunched, repeated the repro. **Result: no improvement** - "does not behave
as a mouse drag." Sending the notification messages a real drag's window
receives isn't enough on its own; whatever DWM actually keys its live-resize
compositing off of isn't just those two messages landing in the window's
queue.

**Ruled out so far, in order tried:** no-resize-at-all (wrong, resize is
real) -> incremental-vs-instantaneous resize (superseded by the outline test)
-> outline masking the transition (disproved directly) -> `SWP_NOCOPYBITS`
(no improvement) -> synthetic `WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` (no
improvement). Three real Win32-level levers have been pulled with no change;
whatever DWM's live-resize path is actually gated on, it isn't reachable by
notifying the target window alone.

**Remaining option, not yet attempted:** actually running a real (or
`SendInput`-driven synthetic) interactive move/size loop - i.e. having Windows
believe the window is genuinely being dragged, not just told it is. This is
a materially bigger change than anything tried so far: it means either
posting `WM_SYSCOMMAND`/`SC_MOVE` and letting `DefWindowProc`'s own loop take
over (which then tracks the *real* cursor, not a programmatic destination),
or driving synthetic mouse movement across the whole gesture via `SendInput`
(visibly moves the system cursor, injects input for the duration of every
move, much bigger surface than the existing low-level hook). Both go
noticeably further against the app's lightweight/non-invasive design bar than
anything tried here, and the side-button passthrough investigation (#2)
already hit a system-freezing failure mode from a much smaller change in this
same input-injection space - any attempt here should be treated with that
same level of caution and tested in a disposable VM first, not the user's
primary machine. Also still worth checking whether the flicker differs by
app (native Win32 vs. Chromium-based apps like the YouTube-in-browser repro)
before going further, since a Chromium-specific cause would make all of this
moot. A related-but-different flicker problem (moving a *maximized* window)
was fixed via `SetWindowPlacement` (see git log), but that fix doesn't apply
here since a maximized window's target size is fixed (the monitor's working
area) in a way a proportionally-sized normal window's isn't.

**Status: PARKED.** User decision after every cheap lever was tried and
ruled out: the only remaining option (fake or drive a real interactive drag)
is a materially bigger, riskier change than this app's design bar or this
issue's importance justify right now. Not scheduled for further work unless
revisited deliberately - if it is, do the risk-scoping this entry already
lays out (disposable VM, treat with #2's level of caution) rather than
jumping straight to code.

## 2. Side-button passthrough - OPEN, PARKED

**Symptom:** holding Mouse4/5 to move a window also fires whatever that button
normally does in the focused app - confirmed in practice (releasing Mouse4 over
a browser navigates it back), since the hook always calls `CallNextHookEx`
regardless. Matters more than it first seemed, since *fetch* (the app's main
use case) is precisely the moment you're about to interact with the window you
just moved. Still open.

**Tried and reverted - do not repeat this exact mechanism:** made
`ButtonComboTracker` flag when a middle click had moved a window during the
current hold, then had `HookCallback` discard the matching `WM_XBUTTONUP`
(return `1`, skip `CallNextHookEx`) instead of forwarding it. Passed unit
tests and built clean, but on first real use **froze the user's mouse
system-wide** - taskbar unresponsive, escalating to left-click dead
everywhere. Killing the process and restarting Explorer did not fix it;
power-cycling the physical mouse did (a full restart would have too).

**Root cause (research-confirmed, see
[LowLevelMouseProc docs](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)
and matching community reports of the same failure shape):** the button-down
was still forwarded normally, and something downstream (most likely
`explorer.exe`) called `SetCapture()` in response to it. Discarding only the
matching button-up meant that capture was never released - and mouse capture
is global/exclusive, so one stuck capture took the whole system's mouse input
down with it. **Rule going forward: never let a down through and then
discard its matching up.** Decide before the down goes out, not after.

**Safe pattern that established tools actually use** (AutoHotkey, X-Mouse
Button Control): suppress the *entire* down+up pair the instant the down is
captured, then replay a synthetic click via `SendInput` if it turns out to be
a plain click (no middle-click follows) - never split a pair. Tag synthetic
events (`LLMHF_INJECTED` / a custom `dwExtraInfo` sentinel) so the hook
doesn't reprocess its own replayed input. A kernel-level driver approach
(Interception) was also considered and rejected - it sits below the message
queue entirely, but has its own documented failure mode (a crash/hang can
lock the physical mouse/keyboard out of Windows, even into Safe Mode) that's
worse than what we already hit.

**Status: PARKED.** The only researched fix (swallow every side-button down
unconditionally, replay via `SendInput` when no move follows) runs extra logic
on every side-button press, not just move gestures, and injects synthetic
input back into the system - more invasive and more hot-path overhead than
the passthrough itself, against the app's own design bar (lightweight,
non-invasive, must not affect Windows' overall performance). If revisited
anyway, build the whole-pair-swallow + replay redesign above and test it in a
disposable VM first - it must not touch the user's primary machine again
until proven safe there.

# Past issues (resolved)

## Rapid repeated moves on the same window race with each other - RESOLVED

**Symptom:** discovered while testing the DPI-compensation fallback (see
below) - moving the same window again before its previous DPI correction
sequence finished caused the window to visibly "queue up" moves and jump
between screens on its own afterward. User's own words: "I can 'queue' up
moves, so that it jumps between screens."

**Root cause, confirmed via `DebugLog` (which needed a fix first - see
below):** each move schedules its own independent correction chain
(`ScheduleDpiCompensationCheck`/`ScheduleDpiCorrectionRetries`) to fix up
apps that don't self-correct their DPI-compensated size. Nothing cancelled
an *earlier* chain when a newer move started on the same window, so if you
moved the window again within a few seconds, both chains ran concurrently -
the stale one would later fire and force the window back to the *previous*
move's target, since it had no idea a newer move had superseded it. Log
data (once tagged with the actual process name) showed exactly this:
`actual` and `fallback` values flipping between the 1080p and 1440p targets
within a 6-second window, all for the same `vlc` window.

**Found via a real debugging misstep worth recording:** while chasing this,
several log entries were wrongly assumed to be Steam (matching earlier
Steam numbers) when they were actually VLC - `DebugLog` didn't record which
process a log line belonged to, only raw rectangles. Fixed by adding
`DescribeWindowProcess` (via `GetWindowThreadProcessId` + `Process.
GetProcessById`) and tagging every DPI-correction log line with `[processname]`.
Any future diagnostic logging in this codebase should identify the
window/process it's about, not just raw values that can coincidentally match
between different apps.

**First tried, incomplete:** changed `pendingDpiCorrections` from a flat
`List<Timer>` to a `Dictionary<IntPtr, Timer>` keyed by window handle, and
had a new correction chain cancel (stop/dispose) whatever chain was already
running for that same window. This stopped the stale-chain-fires-later
symptom, but testing surfaced a second, subtler bug from the same root
cause: a brand new move reads the window's *current* size as its
proportional baseline via `DetermineWindowBounds`/`GetWindowRect`, and if an
earlier move's correction hadn't actually finished *settling the window*
yet (only its *timer* had been cancelled), that baseline is a transient,
still-wrong value - so sizing errors compound across each quick move. Real
data: repeated 1080p<->1440p moves on `vlc` shrunk from a correct 972 width
down to 778 (almost exactly x0.8, one extra deflate ratio bleeding through
from an unsettled prior move) purely from moving quickly, no new hypothesis
needed - the mechanism was already understood, just not fully addressed by
cancelling the timer alone.

**Actual fix, confirmed on real use:** `pendingDpiCorrections` now stores
`(Timer, FallbackBounds)` per window. `ResolvePendingDpiCorrection` - called
at the very top of `MoveWindowToScreen`, *before* `DetermineWindowBounds`
reads anything - stops any pending timer for that window and, if the window
isn't already at its known-correct target, forces it there synchronously
right then. This guarantees every new move always calculates from a
settled, correct baseline, never a mid-flight one. User confirmed rapid
back-and-forth moves on `vlc` no longer drift: "seems quite robust."

## Non-maximized window size changes unexpectedly between the user's two monitors - RESOLVED

**The user's hardware, for reference - stop asking:** a 1440p monitor at 125%
Windows scaling, and a 1080p monitor at 100% scaling.

**Symptom:** moving a non-maximized window between these two monitors gives
the wrong size. Reported forms so far: way too big; ~20% short on height
moving 1440p->1080p; scaling not respected at all (moving either direction,
after compensation was removed). Root cause in play: `CompensateForTarget
DpiResponse` inflates/deflates the size, betting the target app's own
`WM_DPICHANGED` handling will correct it back afterward.

**Tried so far - keep this updated, don't repeat a row:**
| Attempt | Result |
|---|---|
| Do nothing different (original `5818b7f` logic) | Way too large moving to 1080p - inflation exceeds the monitor |
| Add `ClampToMonitor` after compensation | Fixed "too large," but ~20% short on height - clamp truncates the value the app then shrinks *again* |
| Switch `target.Bounds` -> `target.WorkingArea` (avoid taskbar overlap) | Same undershoot persisted, now on both directions |
| Remove `CompensateForTargetDpiResponse` entirely | Scaling not respected at all - the app really does auto-correct, so leaving it uncompensated is wrong |
| Clamp the *intended* size first, compensate that clamped value after (current) | User confirmed working - for whichever app was tested at the time |

**Reopened:** user hit a case moving 1440p@125% -> 1080p@100% where the
window came out too big and **stayed too big** - not a transient flash that
then corrected, a persistent oversized window. Confirmed this is the
compensation bet not paying off (see `CompensateForTargetDpiResponse`'s own
comment: "a bet that doesn't pay off for every app"), for whatever app was in
use at the time.

**The real conflict, now confirmed both ways:** `WindowPlacement.ClampToMonitor`'s
comment states the design intent plainly - "a window bigger than the screen
it's on is not [fine]" - but the current code only clamps *before*
compensation (`intendedSize`), not the final post-compensation value, so
nothing actually enforces that promise.

**Retracted framing:** this entry originally said there was "no single clamp
placement correct for both kinds of app" and to not touch it without solving
that first - overstated, and not actually worked through with the numbers
before writing it down. Walking the math: a second clamp on the final
(post-compensation) size only ever changes anything for a window that had
*zero* headroom left after the first clamp (already filling its monitor's
full height/width). For every window with headroom - which is most windows,
including the left-snapped 0.5-ratio case below - the inflated value never
reaches the monitor edge, so a second clamp is a complete no-op there; the
~20% undershoot is confined to that one narrow no-headroom case, not a
general tradeoff across all windows. Weighed against "otherwise nothing
stops the window from rendering bigger than the monitor it's on," that's not
a real conflict - it's a missing safety check.

**Tried:** added a second `WindowPlacement.ClampToMonitor` call on `size`
after `CompensateForTargetDpiResponse` returns (`DetermineWindowBounds` in
`WindowMoverFinal/Program.cs`), rebuilt Release, killed the stale instance,
relaunched. **Confirmed on real use** - fixed the full-height/full-width
overshoot-past-the-edge case.

**Concrete repro, narrows the cause:** an unmaximized window stretched to
fill the full height of the 1440p monitor (top edge to bottom edge, not
maximized, width unchanged - e.g. a vertically-snapped or manually
full-height-resized window), moved to the 1080p monitor, comes out oversized
and **doesn't fit** - and grew "in all aspects," not just height. Moving it
back to the 1440p monitor *by hand* (plain mouse drag, not WindowMover)
leaves it still oversized. That last part is the key fact: the inflated size
isn't a transient miscalculation the app corrects later, it became the
window's real, persisted size, unaffected by a subsequent unrelated move.
For this window, `CompensateForTargetDpiResponse`'s bet didn't just fail to
help - it actively wrote a wrong, sticky size into the window with nothing
ever correcting it back down.

**Correction - the inflation isn't specific to full-height windows:** a
second repro nailed this down further and disproves the "no headroom to
clamp away" explanation above. A window snapped to the *left half* of the
1440p monitor (width ratio ~0.5, well clear of any clamp limit), moved to
1080p, stays snug against the left edge (position is right) but its **width
grows ~20%** - eating into the center of the screen well past the halfway
line a snapped-left window should stop at. That window had plenty of
headroom and got clamped to nothing (0.5 is nowhere near the monitor's
limit), yet it still inflated by roughly the same ~20-25% the DPI ratio
predicts (120/96 = 1.25). So the inflation isn't gated on headroom at all -
`CompensateForTargetDpiResponse` inflates uncorrected regardless of how much
room the pre-compensation size had; what headroom actually determines is
just whether the result happens to *also* exceed the monitor's edge (the
full-height case) or merely grows into space it shouldn't while staying
on-screen (the half-width snap case, "breaks sizing integrity" without
running off-screen). Same single root cause, two different visible shapes of
symptom depending on how much slack the window started with.

**Confirmed per-app split - named, not theoretical anymore:** Chrome ends up
the correct size after a move (it self-corrects on `WM_DPICHANGED`, matching
`CompensateForTargetDpiResponse`'s assumption). VLC does not - it stays at
whatever size WindowMover set, uncorrected, which is exactly the persistent-
oversized behavior reported earlier in this issue. So the "bet doesn't pay
off for every app" caveat that's been sitting in the code comments all along
has a real, reproducible example on each side now: **Chrome cooperates, VLC
doesn't.**

**This sizing decision is now unit tested.** The whole clamp/compensate/clamp
chain was extracted out of `Program.cs` (which can't be unit tested - it
calls live Win32 DPI APIs) into `WindowPlacement.DetermineTargetSize` and
`WindowPlacement.ScaleForDpiChange` in `WindowMover.Core`, which can.
`WindowPlacementTests.cs` encodes real scenarios with concrete numbers,
including the user's actual monitors' effective DPI (120 vs 96). Anyone
changing this logic should update these tests and run `dotnet test
WindowMover.Tests` first - it catches in milliseconds what this session spent
several rebuild/relaunch/repro cycles on real hardware to find.

**More confirmed apps (later corrected - see the architecture-change section
below):** Explorer folder windows self-correct correctly, same as Chrome.
Steam does not, same as VLC.

**Tried:** rather than accept the left-snap/VLC-shaped growth as permanently
unfixable, added a runtime fallback. `DetermineWindowBounds` now also
returns the bounds the window *would* have gotten without DPI compensation
(`FallbackIfUncorrected`, null when compensation didn't change anything).
When non-null, `MoveWindowToScreen` schedules a one-shot
`System.Windows.Forms.Timer` via `ScheduleDpiCompensationCheck` - the mouse
hook runs on the same STA/message-loop thread `Application.Run()` pumps, so
this is safe without blocking the hook or injecting any synthetic input.
When it fires: if `GetWindowRect` shows the window reached the *correct*
target, leave it alone; otherwise force it there.

**Confirmed on real use, revised several times - kept row-by-row so a wrong
theory doesn't get re-tried:**
| Attempt | Result |
|---|---|
| 400ms delay, exact rect equality vs. what was originally set | VLC fixed. **Chrome broke** - the fallback fired before Chrome's own (slower than 400ms) correction landed, then Chrome's correction applied on top of the fallback's result, compounding into a wrong final size. |
| 1500ms delay, exact rect equality | Chrome/VLC both confirmed correct. Steam still broken - misread at the time as DWM border-margin noise failing an exact-equality check. |
| 1500ms delay, tolerance-based equality (8px) vs. what was originally set | Steam still broken. Added permanent `DebugLog` diagnostic logging instead of guessing further - real data showed Steam drifting 63px on its own, an unrelated internal adjustment nowhere near the real target, bigger than the tolerance and not DWM noise. |
| 1500ms delay, tolerance-based equality vs. the *correct target* instead of what was originally set | The real fix for the false-positive: check whether the window reached the right answer, not whether it merely differs from the original guess. |
| Bounded retry: up to 3 attempts, 1.5s apart | Retracted theory along the way: "only Steam's first-ever DPI-crossing move fails, then it settles" (a one-time warm-up) - looked right from limited data, disproved by more of it. Also retracted: "Steam refuses to shrink below its current size, but grows fine, permanently" - and a priming fix (grow briefly right before a shrink) built on that theory, confirmed to make no difference, then removed. |

**Actual confirmed root cause for the remaining Steam case - not a
WindowMover bug at all:** the user tried to manually drag-resize the same
Steam window smaller by hand, independent of WindowMover entirely, and
**could not**. Steam itself enforces a genuine minimum window size (a real
`WM_GETMINMAXINFO` constraint, almost certainly tied to its current UI/panel
state, which is why it seemed to vary across sessions and looked
direction-dependent). No external mechanism, ours or a manual drag, can
shrink a window below an app's own enforced minimum - every earlier "shrink
succeeded/failed" observation was just Steam's own minimum happening to be
smaller or bigger at that moment, not anything WindowMover was doing right
or wrong.

**`DebugLog` is a permanent utility, not a one-off.** Kept deliberately
(explicit request) as `Program.DebugLog.Write(...)`, logging to
`%TEMP%\windowmover-debug.log`, best-effort (a logging failure must never
affect a real move). Use it for any future hard-to-reproduce runtime
behavior in the untestable Win32 half - don't re-invent an ad-hoc version.

**Final status: RESOLVED.** The DPI-compensation overshoot, the left-snap/
growth case, and the non-cooperating-app case are all fixed and confirmed
working for every app tested (VLC, Chrome, Explorer, Steam-when-growing).
Steam's refusal to shrink below its own enforced minimum is not a bug in
this app - same as a manual drag can't do it either - and needs no further
work. This took several wrong turns before landing here; each one is kept
above, retracted plainly, rather than erased, so none of them get re-tried.

**Follow-up: made the common case feel instant, not part of this issue's core
fix.** The 1500ms delay is structurally necessary for Chrome/Explorer-shaped
apps - their own correction is asynchronous and unconditional (Chrome always
shrinks whatever size it's given by the DPI ratio, so the inflated "bet"
value is the *only* size that lands correctly once Chrome's own shrink
applies), so there's no way to know synchronously when it's safe to check.
Rather than pick one delay, `ScheduleDpiCompensationCheck` now checks fast
first (150ms, non-destructive - a miss just falls through) via
`ScheduleDpiCorrectionRetries` for the proven-safe 1500ms x3 retry path.
Most cooperating apps should now resolve near-instantly since they typically
react well under 150ms; the non-cooperating case is unaffected, since it was
always going to fall through to the safe path anyway. **Not yet confirmed on
real use** - needs Chrome/Explorer/VLC/Steam all re-tested to confirm
nothing broke and the common case actually feels faster.

**Architecture change: the DPI-ratio "bet" is retired entirely.** Prompted
by the user asking, repeatedly and rightly, "why can't we just set the
correct size immediately?" The original answer was structurally true at the
time it was written (a cooperating app auto-resizes itself by the DPI ratio
the moment it detects a DPI change *regardless* of what size it's handed, so
handing it the plain correct size would make it shrink an already-correct
value again) - but that answer only held because, back when compensation was
first built, there was no reactive correction mechanism yet to catch and fix
that self-inflicted shrink afterward. That mechanism now exists (built for
the VLC/Steam non-cooperating case, above) and generalizes cleanly to the
opposite direction:

- `WindowPlacement.DetermineTargetSize` no longer takes any DPI parameters or
  calls a `ScaleForDpiChange`-style ratio multiply - it's just
  `ClampToMonitor(ProportionalSize(...))`, the plain correct size, always.
  `ScaleForDpiChange` is deleted from `WindowMover.Core` entirely.
- `MoveWindowToScreen` sets that plain correct size directly via
  `SetWindowPos`, unconditionally - no more inflated/deflated guess.
- Whenever the move crosses a real DPI boundary on a per-monitor-DPI-aware
  window (`CrossesRealDpiBoundary`, replacing `TryGetDpiCompensation`),
  `ScheduleDpiCompensationCheck` reactively watches - using the exact same
  fast-check/retry machinery already built - and corrects the window *back*
  to that same plain correct size if anything changes it away.

This flips which category of app needs the visible correction step: a
non-cooperating app (VLC, Steam) now lands correct on the very first
`SetWindowPos` call, with **no correction step needed at all** - not "fixed
faster," genuinely zero extra resize. A cooperating app (Chrome, Explorer)
now needs the reactive correction to catch its own self-inflicted shrink and
correct it back up, the mirror image of what used to happen. Net effect:
strictly fewer visible resize events for the (likely more common in
practice) non-cooperating case, one fewer DPI-lookup-and-multiply step in
the code, and no more needing to know real DPI values at all for sizing
(`CrossesRealDpiBoundary` only needs to know DPI *differs*, not by how much).
`WindowPlacementTests.cs` updated to match - the DPI-ratio-specific tests
are gone, replaced with tests for the plain clamped-proportional composition.

**Confirmed on real use.** VLC: lands correct immediately, every check a
"fast check, 150ms, already matches" - genuinely zero correction step, as
predicted. Chrome: confirmed via log that its own self-resize is caught and
corrected back to the right size every time (both directions - Chrome grows
itself when moving to a higher-DPI monitor, shrinks when moving to a
lower-DPI one, catches and fixes both), just via the ~1.5s retry path rather
than instantly, so there's a visible wrong-then-correct jump for Chrome now
- exactly the traded-off cost described above, not a bug.

**Final status: this issue (and its earlier "make it instant" follow-up) is
fully resolved by this redesign**, which replaced both at once - there's no
longer a DPI-ratio bet to make instant or delay-tune; there's just a plain
correct size and a reactive catch for whichever apps change it away from
that.

**Correction to an earlier claim: Explorer was mis-categorized.** "Explorer
self-corrects, same as Chrome" (above) was based on visual-only observation
under the old bet-then-fallback system, where a self-correcting app and a
non-cooperating app that our own fallback quietly fixed would have looked
*identical* from the outside - both just end up at the correct size. That
test couldn't actually distinguish the two causes. Under this redesign,
Explorer lands correct with **zero correction step** (same as VLC's "fast
check, already matches" pattern), not via a caught-and-corrected self-resize
like Chrome's. Stronger evidence now points to Explorer never resizing
itself at all - it was VLC-shaped all along, not Chrome-shaped. Confirmed
apps by actual behavior: **self-resizes (needs reactive correction):
Chrome. Doesn't touch its own size (lands correct immediately): VLC,
Explorer, Steam** (aside from Steam's separate minimum-size limit on
shrinking, unrelated to this).
