# Window Mover

A Windows system tray utility that moves windows between monitors using the side
buttons on your mouse.

## Why

Windows can already cycle a focused window to the adjacent monitor with
`Win+Shift+Arrow`, but that means reaching for the keyboard. WindowMover does
the same hop, plus the one thing nothing built-in does at all - straight to
wherever the cursor happens to be - entirely from the mouse: hold a side
button, middle-click.

What exists instead, and where they fall short:

- **DisplayFusion** — its Mouse Cursor Functions move the cursor, not windows. The
  closest match is a custom script their staff wrote for one user on request, not
  a shipped feature.
- **PowerToys** — an open, related request,
  [#22165 "Gather Windows"](https://github.com/microsoft/PowerToys/issues/22165)
  (open since November 2022), framed around remote-desktop scenarios rather than
  cursor position specifically. Nothing shipped yet.
- **AutoHotkey** — [scripts exist](https://www.autohotkey.com/boards/viewtopic.php?t=76122)
  that do this, but with real gaps: no monitor-bounds check (a window can end up
  straddling two monitors), no filtering of unsafe windows (taskbar, tool windows,
  tiny system UI), and maximized windows are skipped entirely rather than
  restored, moved and remaximized.

WindowMover is this feature on its own: free, and nothing to write or maintain
like an AutoHotkey script. `WindowMoveFilter`, `MonitorLayout` and
`WindowPlacement` are covered by the test suite against exactly the gaps
listed above.

## Controls

| Shortcut | Action |
|---|---|
| Mouse5 + middle click | Move the window to the next monitor |
| Mouse4 + middle click | Move the window to the monitor your cursor is on |
| Mouse4 + Mouse5 + middle click | Same as Mouse4 alone - the monitor your cursor is on |

Mouse4 is the back thumb button and Mouse5 the forward one, on most mice.
Mouse4 always wins when both are held - Mouse5 only gets its own meaning
(cycle to the next monitor) when held on its own.

The taskbar, desktop icons, tool windows and very small UI elements are skipped.

**Where to put the cursor:** WindowMover captures whichever window is currently
active the instant you press Mouse4/Mouse5, not whatever's under the cursor.
If your cursor is resting over a *different* window at that moment, that
click can activate it first, and WindowMover ends up moving that window
instead of the one you meant. Hovering over the target window's **taskbar
icon** when you press the side button avoids this - clicking there doesn't
hand focus to some other on-screen window.

## Installation

Run `WindowMover.exe`. It appears in the system tray. Right-click the tray icon for:

- **Start with Windows** — toggle auto-launch on login
- **Hide window during monitor-crossing resize** — on by default. Some apps
  briefly resize themselves wrong right after a monitor-crossing move before
  WindowMover corrects them; this hides the window for that moment instead of
  showing the wrong size. Trade-off: hiding is a real visibility change, and
  at least one app (Chrome, for YouTube's spacebar-to-pause) reacts to it by
  dropping its own keyboard focus, even though general typing is unaffected.
  Turn this off if that bothers you.
- **About** — controls and current settings
- **Exit**

Only one instance runs at a time.

## How it works

A low-level mouse hook (`WH_MOUSE_LL`) intercepts Mouse4 and Mouse5 events
system-wide. When a side button is held and the middle button is pressed, the
window moves to the target monitor. A maximized window is moved directly via
`SetWindowPlacement` onto the target monitor's own working area, still
maximized, rather than visibly restoring and re-maximizing.

Some apps resize themselves the moment they detect a DPI change between
differently-scaled monitors, overriding the size WindowMover just set.
`DpiCorrectionScheduler` watches for that reactively (via
`EVENT_OBJECT_LOCATIONCHANGE`, not polling) and corrects it back.

## Project layout

| Project | What it is |
|---|---|
| `WindowMoverFinal` | The Windows Forms app: P/Invoke, the mouse hook, the tray icon |
| `WindowMover.Core` | The logic that decides which window moves and where it lands |
| `WindowMover.Tests` | xUnit tests for the core |

## Building and testing

Requires the .NET SDK. The app targets `net10.0-windows` and must be built on
Windows.

```
dotnet build
dotnet test
```

To produce the executable:

```
dotnet build WindowMoverFinal/WindowMoverFinal.csproj -c Release
```

It lands in `WindowMoverFinal/bin/Release/net10.0-windows/`.

## License

MIT
