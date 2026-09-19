# Known issues and investigation history

Working notes on open problems with WindowMover, kept so we stop re-deriving the
same facts. Update this file as issues get resolved or new facts are found -
don't let it go stale. Resolved issues move to "Past issues" below, in full,
rather than being deleted - the history is what stops us re-deriving facts.

# Current issues

No open issues.

# Past issues (resolved)

## #7 - Every maximized move was logged under the wrong reason, 2026-09-19

Found while splitting `WindowMoveActions` and `DpiCorrectionScheduler` into one
file per job. The watch that ends a move has two kinds: one with a size to
police (a DPI correction) and one without (a maximized move, waiting only for
the window to stop moving so it can be revealed). `OnSettled` tested
`IsZoomed(hwnd)` before asking which kind it was - and a maximized move ends
with the window maximized, that being the entire point of it. So every
maximized move took the zoomed branch and logged `now maximized, abandoning
correction`, and the message written for the maximized case was unreachable
code.

Nothing misbehaved: both branches end the watch through the same choke point,
so the window was always revealed. The cost was the log, which is the only
instrument this half of the app has - the DPI self-correction quirks in #3 were
found through it. Measured: 261KB of accumulated log contained zero occurrences
of `window settled after a maximized move, revealing`. After reordering the two
checks, the first live run produced it.

The tests did not catch this and could not have: they assert on what the window
ends up doing, which was right the whole time. A log line that names the wrong
cause is only visible to someone reading the log against the code that wrote it.
Log lines now also say which kind of watch they came from (`Move watch` vs `DPI
correction`), so the two cannot be confused again, and the variable that `wanted=`
prints is called that in the code too - it used to print `fallback=`, a word left
over from a design that no longer exists.

## #6 - A moved window cannot be made to come to the front, 2026-09-19

Moving a window onto a monitor that already had windows on it could leave it
behind them, which looks exactly like a move that never happened. The obvious
answer - bring the moved window to the front - was built, and then measured,
and Windows does not allow it:

- **Activation is refused.** A process that does not hold the foreground cannot
  take it while the user is interacting with another app. The documented escape
  hatch, attaching to the foreground thread's input queue with
  `AttachThreadInput`, was tried and also refused. The app's own log, recorded
  while the machine was in use: `activated via input attach=False`, every
  attempt, without exception.
- **Raising is refused past the foreground window.** A raise is normally free of
  the foreground lock, and it works against every other window - but not past
  the active one. Measured in the live suite: `SetWindowPos(HWND_TOP)` on the
  moved window left it one position *below* the window already there, and
  raising it again from the test process made no difference either - while
  *lowering* the other window worked immediately. Neither window was topmost and
  neither owned the other, so ordering was not the constraint; permission was.

The feature was removed rather than left as something that works when nobody is
at the keyboard. In its place the app now draws where the window went: a brief
outline at the window's new bounds with the monitor's number in it, fading out
over about half a second (`MoveIndicator`).

That works precisely because it is this app's own window. Always-on-top is
allowed for one's own window, so it shows above whatever is in the way; it is
`WS_EX_NOACTIVATE` so it never takes focus, `WS_EX_TRANSPARENT` so clicks pass
straight through it, and `WS_EX_TOOLWINDOW` so it stays out of the taskbar and
Alt-Tab - which also means `WindowMoveFilter` refuses to move it, so the app can
never be asked to throw its own indicator across the desk. A live test asserts
that last part rather than trusting it.

One real finding survives from the removed code and is worth keeping: holding
the foreground does **not** mean being the top window. The old bring-to-front
skipped its raise whenever the window already had focus, on that assumption, and
a window can sit above the active one.

## #5 - Hiding a window for a correction took its taskbar button with it, 2026-09-19

`ShowWindow(hwnd, SW_HIDE)` was how a window was hidden for the duration of a
DPI correction. A hidden window is not visible as far as Windows is concerned,
and the shell drops a window's taskbar button the moment that becomes true - so
every corrected move flashed the taskbar, and the button could come back in a
different position. The constant's own comment claimed "no taskbar flash",
which was simply wrong.

Two alternatives were measured against a real window on the second monitor:

| Technique | Cross-process | Window invisible | Stays in taskbar |
|---|---|---|---|
| `DwmSetWindowAttribute(DWMWA_CLOAK)` | **no** - `E_ACCESSDENIED` (0x80070005) | - | - |
| `WS_EX_LAYERED` + `SetLayeredWindowAttributes(alpha 0)` | yes | yes | yes |

The layered measurement, from a screen capture of the window's own rectangle
rather than by eye: pixel difference of 117.65 against the same region while
transparent (it really vanished), 0 after restoring (it came back unchanged),
and `IsWindowVisible` stayed true throughout - which is the property the shell
keys the taskbar button off.

So the hide is now transparency. It is refused for a window that is already
layered: such a window manages its own transparency, and the alpha it had
cannot be read back in a form this could restore, so those windows keep the
visible flash rather than risk being left permanently altered.

Expected but **not** verified: the old tradeoff behind the tray toggle's
warning - Chrome treating `SW_HIDE` as backgrounding the page, which
interrupted YouTube's spacebar-to-pause - should be gone with the mechanism
that caused it, since no window state changes any more. Worth confirming
against YouTube before treating it as settled.

`A_window_hidden_during_correction_never_leaves_the_taskbar` in
`WindowMover.LiveTests` pins the property down: across the whole correction the
window must never stop being visible to Windows, must actually have been
hidden, and must not be left layered afterwards.

## #4 - A maximized window would not move at all (VS Code), 2026-09-19

Reported as "it's bugged, the window just isn't moving", against VS Code.
Every gesture was reaching the app correctly - the log shows the side button,
the middle click, the resolved command and the target monitor, over and over,
each one targeting DISPLAY2 because the window never left DISPLAY1.

Measured directly, starting from a window **maximized on the primary monitor**
and asking for the second monitor:

| Sequence | Notepad | Edge | VS Code |
|---|---|---|---|
| `SetWindowPlacement` alone | nothing | nothing | nothing |
| `SetWindowPlacement` + minimize/maximize | moves | moves | **nothing** |
| restore -> `SetWindowPos` onto target -> maximize | moves | moves | moves |

Edge is in that table deliberately. The first explanation reached for was
"Chromium reasserts its own window state" - and it is wrong: Edge is Chromium,
was measured against the same three sequences on a clean profile, and moved
where the broken sequence put it. Whatever VS Code is doing, sharing Chromium
is not it, and a test built on "any Chromium window" would have passed against
the broken code. Measured on a clean `--user-data-dir` with extensions
disabled, so it is not something stale in a profile either.

Two facts behind that:

- A maximized window ignores new bounds, and `rcNormalPosition` only says where
  it lands when it *leaves* the maximized state - so `SetWindowPlacement` alone
  can never move one. The minimize/maximize that followed it was not a cosmetic
  refresh, as the old comment had it; it was the only reason the path worked at
  all.
- VS Code reasserts its own idea of where the window belongs as it re-enters the
  maximized state, so the minimize/maximize that rescued Notepad put it straight
  back where it started. Why it does and Edge does not was not chased down - the
  sequence that works everywhere made it moot.

Fixed by always taking the window out of the maximized state onto the target
monitor and maximizing it there, which worked for every app tested. The landing
rectangle is the target monitor's working area, so the intermediate frame is
close to the final size and the move reads as a jump rather than a shrink-jump-
grow. `SetWindowPlacement`, `WINDOWPLACEMENT` and
`WindowPlacement.ToWorkspaceCoordinates` were removed with it.

Two things let this sit unnoticed, both since fixed:

- **Nothing was logged.** Every silent exit was silent in the log too, so "the
  window didn't move" and "the gesture never fired" looked identical. Gestures,
  refusals and failed Win32 calls are now all logged.
- **Nothing tested it.** The unit suite covers the decisions (which monitor,
  what size) and cannot see whether Windows honoured them.
  `WindowMover.LiveTests` now drives the real move code against real windows,
  including a real VS Code window on a throwaway profile - an ordinary test
  window behaves like Notepad and passes against the broken code, which was
  confirmed by running the new suite against it: only the VS Code test failed.

Worth remembering: the fault was reported against the app that *couldn't* move,
while every other window moved fine, and the first guess (Z-order - the window
landing behind another) was wrong. The log said so in one line once it existed.

# Older past issues

None yet.

