# Known issues and investigation history

Working notes on open problems with WindowMover, kept so we stop re-deriving the
same facts. Update this file as issues get resolved or new facts are found -
don't let it go stale. Resolved issues move to "Past issues" below, in full,
rather than being deleted - the history is what stops us re-deriving facts.

# Current issues

No open issues.

# Past issues (resolved)

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

