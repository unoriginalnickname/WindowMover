# Window Mover

A Windows system tray utility that moves windows between monitors using the side
buttons on your mouse.

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
