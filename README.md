# Window Mover

A lightweight Windows system tray utility that moves windows between monitors using
the side buttons on your mouse.

## Features

- Move any window to the next monitor with a button combo
- Move a window directly to whichever monitor your cursor is on
- Runs silently in the system tray — no taskbar clutter
- Optional "Start with Windows" support via the registry
- Configurable default window size for moved windows
- Skips the taskbar, desktop, tooltips and other system UI

## Controls

| Shortcut | Action |
|---|---|
| Mouse4 + middle click | Cycle the window to the next monitor |
| Mouse5 + middle click | Cycle the window to the next monitor |
| Mouse4 + Mouse5 + middle click | Move the window to the monitor your cursor is on |

Mouse4 is the back thumb button and Mouse5 the forward one, on most mice.

The window is captured when you press the side button, not when you middle-click —
by the time you click, the foreground window may have changed.

## Installation

1. Download or build the executable (see below).
2. Run `WindowMover.exe`. It appears in the system tray.
3. Optionally enable **Start with Windows** from the tray icon's right-click menu.

## Usage

Right-click the tray icon for:

- **Start with Windows** — toggle auto-launch on login
- **Set Window Size** — width and height for moved windows (default 800×600)
- **About** — controls and current settings
- **Exit**

## How it works

A low-level mouse hook (`WH_MOUSE_LL`) intercepts Mouse4/Mouse5 events system-wide.
When a side button is held and the middle button is pressed, the captured window
moves to the target monitor — restored first if it was maximized, and re-maximized
afterwards.

Windows that cannot sensibly be moved are skipped: the taskbar, invisible windows,
tool windows, very small UI elements and desktop icons.

## Project layout

| Project | What it is |
|---|---|
| `WindowMoverFinal` | The Windows Forms app — P/Invoke, the mouse hook, the tray icon. A thin adapter over the core. |
| `WindowMover.Core` | The decision logic, plain `net8.0` with no Win32 or WinForms dependency: which windows may move, which monitor is next, where on it a window lands, and what a button combo means. |
| `WindowMover.Tests` | xUnit tests for the core. They run without a desktop — no monitors or windows needed. |

The split exists so the interesting logic can be tested. Deciding *where a window
should go* is arithmetic over rectangles and does not need a real monitor; only
*putting it there* needs Win32. `WindowPlacement.PlanMove` returns a plan —
target bounds plus whether to restore before and maximize after — and the adapter
carries it out.

## Building and testing

Requires the .NET SDK. The app targets `net10.0-windows`, so building it needs
Windows; the core and its tests target `net8.0`.

```
dotnet build
dotnet test
```

To produce the executable:

```
dotnet build WindowMoverFinal/WindowMoverFinal.csproj -c Release
```

It lands in `WindowMoverFinal/bin/Release/net10.0-windows/`.

You can also open `WindowMoverFinal.slnx` in Visual Studio 2022 or later.

## Notes

- Only one instance runs at a time, enforced with a named mutex.
- The window size set from the tray menu applies for that session and resets to
  800×600 on restart.
- Windows only — it uses the Win32 API through P/Invoke.

## License

MIT
