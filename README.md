# Window Mover

A Windows system tray utility that moves windows between monitors using the side
buttons on your mouse.

## Why

Windows can move a focused window to the adjacent monitor with `Win+Shift+Arrow`,
but only one hop at a time, in a fixed direction. Nothing built-in sends a window
straight to whichever monitor the cursor is on.

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

WindowMover is this feature on its own: free, five files, triggered by a mouse
chord (hold a side button, middle-click) instead of a hotkey or a script to write
and maintain. `WindowMoveFilter`, `MonitorLayout` and `WindowPlacement` are covered
by the test suite against exactly the gaps listed above.

## Controls

| Shortcut | Action |
|---|---|
| Mouse4 + middle click | Move the window to the next monitor |
| Mouse5 + middle click | Move the window to the next monitor |
| Mouse4 + Mouse5 + middle click | Move the window to the monitor your cursor is on |

Mouse4 is the back thumb button and Mouse5 the forward one, on most mice.

The taskbar, desktop icons, tool windows and very small UI elements are skipped.

## Installation

Run `WindowMover.exe`. It appears in the system tray. Right-click the tray icon for:

- **Start with Windows** — toggle auto-launch on login
- **Set Window Size** — width and height for moved windows (default 800×600,
  resets on restart)
- **About** — controls and current settings
- **Exit**

Only one instance runs at a time.

## How it works

A low-level mouse hook (`WH_MOUSE_LL`) intercepts Mouse4 and Mouse5 events
system-wide. When a side button is held and the middle button is pressed, the
window moves to the target monitor. A maximized window is restored first and
re-maximized afterwards.

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
