# Known issues and investigation history

Working notes on open problems with WindowMover, kept so we stop re-deriving the
same facts. Update this file as issues get resolved or new facts are found -
don't let it go stale.

## 1. Side-button passthrough

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

## 2. Non-maximized window moves flicker more than a plain drag

**Symptom:** moving a normal (non-maximized) window with WindowMover visibly
flickers/redraws noticeably more than just dragging the same window with the
mouse. Not yet investigated in depth.

**Likely cause, not yet confirmed:** `MoveWindowToScreen`'s non-maximized path
changes size and position in the same `SetWindowPos` call, since proportional
sizing (see the mixed-DPI history in git log) means the window is almost
never the same pixel size on the target monitor. A plain drag only ever
changes position - DWM can composite that as a cheap transform of the
window's existing bitmap with no repaint. A size change forces the target
app to actually redraw its contents at the new size, which is plausibly the
extra visible flicker. If so, this is a cost coupled to the proportional-
sizing feature itself, not a separate bug to fix independently - trading
some move smoothness for correctly-sized windows across monitors was already
a deliberate choice, not an accident.

**Not yet investigated:** whether the redraw cost can be reduced without
giving up proportional sizing. Worth testing directly rather than assuming -
a related-but-different flicker problem (moving a *maximized* window) was
fixed via `SetWindowPlacement` (see git log), but that fix doesn't apply here
since a maximized window's target size is fixed (the monitor's working area)
in a way a proportionally-sized normal window's isn't.

## 3. Smaller items

- **Tray icon**: ships with `SystemIcons.Application` (generic) and a stale
  `// Replace with your turtle icon if desired` comment.
- **Window size persistence**: the fallback size resets to 800x600 on every
  restart. `WindowWidth`/`WindowHeight` (`Program.cs:38-39`) are plain
  in-memory `static int` fields, never read from or written to the registry -
  confirmed still true, not yet revisited since proportional sizing became
  the primary behavior for the common case.
