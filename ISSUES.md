# Known issues and investigation history

Working notes on open problems with WindowMover, kept so we stop re-deriving the
same facts. Update this file as issues get resolved or new facts are found -
don't let it go stale.

## 1. Proportional window sizing on mixed-DPI monitors — RESOLVED (confirmed working on real hardware)

**Goal** (confirmed with the user): when a non-maximized window moves to another
monitor, it should keep occupying the same *percentage of screen* it did before,
not the same fixed pixel size, and not necessarily the same absolute pixel size
that plain drag-and-drop preserves.

**What's implemented so far:**
- `WindowMover.Core/WindowPlacement.cs`: `ProportionalSize(sourceMonitorBounds,
  currentWindowSize, targetMonitorBounds)` - computes width/height ratios against
  the source monitor and applies them to the target monitor, independently per
  axis. Unit-tested (5 tests in `WindowPlacementTests.cs`), and the math is
  provably correct in isolation - ratio in equals ratio out, by construction.
- `WindowMoverFinal/Program.cs`: `DetermineWindowSize()` wires this in for
  non-maximized windows (maximized windows keep using the fixed configured size,
  since size barely matters there - maximize overrides it anyway). Falls back to
  the fixed configured size if the window's current bounds/monitor can't be read.
- `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` added at the very top of
  `Main()`, before the mutex/any window handle. Confirmed via research: this is
  the *correct* mechanism for this codebase specifically, because
  `WindowMoverFinal`'s hand-written `Program.cs` never calls
  `ApplicationConfiguration.Initialize()`, so the alternative
  (`<ApplicationHighDpiMode>` in the csproj) would silently do nothing here.
  Confirmed via Microsoft docs: once PerMonitorV2 is active, `Screen.Bounds` and
  `GetWindowRect` should report true physical pixels directly, no manual
  `GetDpiForMonitor` conversion needed. No manifest is present or needed (one
  would actively block the API call - warning WFAC010).

**Real-world test result on the user's actual hardware (1080p @ 100% + 1440p @
125%): still broken after the PerMonitorV2 fix.** The window consistently comes
out too LARGE while sitting on the 125% monitor and too SMALL while sitting on
the 100% monitor - **regardless of which direction it moved** (user explicitly
confirmed "directionality doesn't matter"). This pattern is diagnostic: a bug in
the ratio math itself would scale with the ratio *between* the two monitors, and
would flip depending on move direction. A symmetric, destination-monitor-tied
distortion that doesn't care about direction points somewhere else entirely.

**CONFIRMED root cause, sourced from Microsoft's own docs, and independently
verified by the user's own test (setting both monitors to matching 100% scaling
fixed the sizing completely):**

`SetWindowPos` moving a window onto a monitor with different DPI triggers
[`WM_DPICHANGED`](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged)
for that window - but only for windows that are themselves per-monitor-DPI-aware
(most modern default Windows apps: Explorer, Edge, Chrome, VS Code, Windows
Terminal, etc.). A well-behaved app receiving that message resizes itself to
preserve its own layout in logical/DIP units - which mechanically needs more
physical pixels on a higher-scaled monitor and fewer on a lower-scaled one. This
happens **asynchronously, on the target app's own thread, after WindowMover's
`SetWindowPos` call has already returned** - so the real sequence is: WindowMover
sets the correct proportional size, then the target app receives the DPI-change
notification moments later and resizes itself again, overriding what we set.
This exactly matches the reported symptom: destination-monitor-tied, direction-
independent, and it vanished entirely once there was no DPI boundary to cross.

**This is a hard architectural ceiling, not a bug in WindowMover.** The app has
no access to another process's internals and cannot intercept or suppress how a
target window responds to its own message. Practical implication: proportional
sizing **works correctly for DPI-unaware/SystemAware target apps** (they don't
receive `WM_DPICHANGED` at all, and just get DWM's blurry bitmap-stretch instead
- our sizing sticks), but is **fundamentally uncontrollable for per-monitor-DPI-
aware target apps** crossing a DPI boundary - which includes most common modern
apps. This isn't fixable by trying harder; it would require fighting the target
app's own resize handling, which isn't something an external mover can do.

**Confirmed by direct test (user set both monitors to matching 100% scaling):
the window sizes correctly with matched scaling.** This isolated the cause
precisely - it was the *mismatch between the two monitors' scaling percentages*
specifically, not a bug in `ProportionalSize`'s ratio math, not a bug in monitor
detection, and not something wrong with the ProportionalSize/DetermineWindowSize
wiring in general.

**FIX IMPLEMENTED AND CONFIRMED WORKING** (`WindowMoverFinal/Program.cs`,
`CompensateForTargetDpiResponse`): since a per-monitor-DPI-aware target window's
default `WM_DPICHANGED` response scales whatever size we set by
`(targetDpi / sourceDpi)`, we pre-divide by that same ratio before calling
`SetWindowPos`, so the two cancel out. Concretely:
- Checks whether the target window is even per-monitor-DPI-aware first
  (`GetWindowDpiAwarenessContext` / `GetAwarenessFromDpiAwarenessContext`) -
  skips compensation entirely for windows that won't react, since compensating
  for a non-reactive window would introduce an error where none exists.
- Reads the source monitor's DPI via `MonitorFromWindow` *before* the window
  moves, and the target monitor's DPI via `MonitorFromRect` on the target
  bounds, both through `GetDpiForMonitor` (Shcore.dll).
- Skips compensation if both monitors share the same DPI (nothing to cancel).
- Confirmed by the user on real mixed-DPI hardware (1080p @ 100% + 1440p @ 125%)
  after re-enabling the mismatched scaling: "works as intended now."

**Known, sourced, un-fixed exception: Windows Terminal.** It overrides the
default `WM_GETDPISCALEDSIZE` behavior to keep its row/column count constant
instead of scaling pixels linearly
([microsoft/terminal PR #18268](https://github.com/microsoft/terminal/pull/18268)),
so this compensation will not land correctly for it specifically. Other apps'
exact handling (Chrome, Edge, VS Code, Explorer) was not individually verified -
plausibly default, not confirmed. This was implemented and accepted with that
caveat stated plainly rather than claimed as a universal fix.

**Ruled out already, don't re-check:**
- Stale/duplicate running instance (confirmed only one PID running throughout).
- Embedded or project app.manifest overriding `SetHighDpiMode` (confirmed: no
  manifest anywhere in the project or build output).
- The DPI gap corrupting the *ratio math itself* under `SystemAware` (an earlier
  research pass concluded this was fine since `Screen.Bounds`/`GetWindowRect`
  are both virtualized into the same logical space under SystemAware - true as
  far as it goes, but insufficient, since the real-world test still failed even
  after fixing this with PerMonitorV2).
- A general bug in the ratio math, monitor detection, or the
  ProportionalSize/DetermineWindowSize wiring (ruled out by the matched-scaling
  test above - the logic is correct when scaling matches).

## 2. Maximize/restore flicker when moving a maximized window — PARKED, accepted as a Windows-level limitation

**Symptom:** moving a maximized window visibly shrinks it to a small restored
size, jumps it to the new monitor, then grows it back to maximized - three
distinct visible states instead of one clean jump. Root cause: `MoveWindowToScreen`
does restore → `SetWindowPos` → maximize as three separate real Win32 calls, each
of which gets drawn.

**Tried and reverted, both denied (no fix, and the second may have made it look
worse):**
1. `DwmSetWindowAttribute(DWMWA_TRANSITIONS_FORCEDISABLED)` bracketing the
   sequence - no visible effect. (This attribute governs other DWM effects, not
   the classic minimize/maximize/restore glide.)
2. Temporarily disabling the "animate windows when minimizing and maximizing"
   system setting (`SystemParametersInfo(SPI_SETANIMATION)`) around the sequence
   - no fix, and suspected of making it look worse (hard instant jumps between
   the 3 real states instead of blended/eased motion).

Both reverted; the code is back to the plain 3-call sequence with no animation
tinkering (current, clean baseline).

**Not attempted:** replacing the 3-call sequence with a single
`SetWindowPlacement` call (set `showCmd = SW_SHOWMAXIMIZED` and a target-monitor
`rcNormalPosition` in one atomic call, so the window never visibly passes through
the small restored state). Real risk: `rcNormalPosition` uses *workspace
coordinates* (an offset from screen coordinates based on the primary monitor's
taskbar position), not the screen coordinates the rest of the app uses - a
real, easy way to introduce a subtle multi-monitor placement bug.

**Research conclusion:** this is a real, known class of Windows bug, not
something specific to WindowMover's code - even Microsoft's own native
`Win+Shift+Left/Right` has a documented visual glitch doing this. A flicker-free
fix is achievable in principle (Telegram Desktop's Qt UI library fixed the same
bug class - see
[desktop-app/lib_ui#368](https://github.com/desktop-app/lib_ui/pull/368)), but
their actual root cause was different from ours (stale DWM-painted margin/
scrollbar pixels, missing repaint on `WM_MOVE`) and needed deep repaint/DPI
coordination, not a simple call swap. No evidence either way that
`SetWindowPlacement` alone would fix WindowMover's case. Even DisplayFusion
(paid, dedicated multi-monitor tool) hasn't fully solved adjacent flicker cases.

**Decision: not worth pursuing further right now** - the confirmed fixes both
failed, the remaining option is unproven and carries real coordinate-math risk,
and even commercial tools haven't fully solved this class of bug. Revisit only
if a concretely-confirmed technique turns up.

## 3. Smaller, not-yet-actioned items

- **Side-button passthrough**: holding Mouse4/5 to move a window also fires
  whatever that button normally does in the focused app (e.g. browser back/
  forward navigation), since the hook always calls `CallNextHookEx` regardless.
  Matters more than it first seemed, since *fetch* (the app's main use case) is
  precisely the moment you're about to interact with the window you just moved.
- **Tray icon**: ships with `SystemIcons.Application` (generic) and a stale
  `// Replace with your turtle icon if desired` comment.
- **Set Window Size UX**: two sequential `Microsoft.VisualBasic.Interaction.
  InputBox` prompts, silently no-ops on invalid/cancelled input with no
  feedback. Also: now that non-maximized windows use proportional sizing, this
  setting's actual scope is just the *fallback* size (used only when maximized,
  or when the window's current bounds/monitor can't be read) - worth
  reconsidering what this menu item should even say/do now.
- **Window size persistence**: the fallback size resets to 800x600 on every
  restart (documented as intentional) - never revisited since proportional
  sizing became the primary behavior for the common case.
