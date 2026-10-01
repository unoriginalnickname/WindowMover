# Window Mover

A Windows system tray utility that moves windows between monitors using only the
mouse.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/pull-window-dark.svg">
  <img src="docs/pull-window-light.svg" alt="Two monitors. A blue window on the left monitor is pulled to the right monitor by clicking its taskbar icon, holding Mouse4 and middle-clicking. Then the left monitor is turned off and the same thing pulls a red window out of it.">
</picture>

## What it does

For a window on a monitor you've turned off, or when no keyboard is in reach. It
has two actions:

1. **Pull** - the window comes to the monitor your cursor is on.
2. **Cycle** - the window goes to the next monitor from the one it is on.

## Controls

| Button | Role |
|---|---|
| Left-click | Puts a window in focus (for example, click its taskbar icon) |
| Hold Mouse4 | Selects the pull action: to the monitor the cursor is on |
| Hold Mouse5 | Selects the cycle action: to the next monitor from the window's |
| Middle-click | Activates the selected action on the focused window |

Mouse4 is the back thumb button and Mouse5 the forward one, on most mice.
The taskbar, desktop icons, tool windows and very small UI elements are never
moved.

## Installation

Run `WindowMover.App.exe`. It appears in the system tray. Right-click the tray icon for:

- **Start with Windows** — toggle auto-launch on login
- **Hide window while it moves** — on by default. The window is made fully
  transparent until it stops moving, which hides the resizing and maximize
  animations described under How it works. It is not hidden with
  `ShowWindow(SW_HIDE)`, so it keeps its taskbar button, its place in the
  Z-order and its focus. Turn it off if you would rather this app never touched
  how your windows are drawn.
- **Show where the window landed** — on by default. A brief outline at the
  window's new position with the monitor's number in it, fading out over about
  half a second. Turn it off for a silent move.
- **About** — controls and current settings
- **Exit**

Only one instance runs at a time.

The executable used to be called `WindowMoverFinal.exe`. If you had **Start with
Windows** turned on under that name, turn it off and on again once - the old
registry entry still points at the old filename.

## How it works

A low-level mouse hook (`WH_MOUSE_LL`) intercepts Mouse4 and Mouse5 events
system-wide. When a side button is held and the middle button is pressed, the
window moves to the target monitor. A maximized window cannot simply be handed
new bounds - it ignores them - so it is restored onto the target monitor's
working area and maximized again there. Those are three visible state changes
and Windows animates each one, so the window is hidden for the whole sequence
and revealed once it stops moving - which is watched for, not waited out on a
guessed timer, because the maximize animation runs on after the call that
started it returns.

It does not bring the moved window to the front, because Windows will not
reliably allow it: a background process is refused the foreground while you are
using another app, and is refused raising a window past the active one as well.
That was built, measured and removed - see ISSUES.md #6. The landing outline is
this app's own window, so it is allowed to draw on top of anything, and it
takes no focus, swallows no clicks and changes nothing about your windows.

Some apps resize themselves the moment they detect a DPI change between
differently-scaled monitors, overriding the size WindowMover just set.
`MoveSettleWatcher` watches for that reactively (via
`EVENT_OBJECT_LOCATIONCHANGE`, not polling) and corrects it back.

## Project layout

| Project | What it is |
|---|---|
| `WindowMover.App` | The Windows Forms app: P/Invoke, the mouse hook, the tray icon |
| `WindowMover.Core` | The logic that decides which window moves and where it lands |
| `WindowMover.Tests` | xUnit tests for the core - pure logic, instant, silent |
| `WindowMover.LiveTests` | xUnit tests that move real windows on the real desktop |

Inside `WindowMover.App`, one folder per job:

| Folder | What is in it |
|---|---|
| `Input/` | `MouseGestureHook` - the low-level mouse hook, turning button events into a move |
| `Moving/` | `MovableWindowCheck` (may this window be moved), `MoveTargetBounds` (where it lands), `WindowMove` (the move itself), `WindowTransparency` (hiding it on the way), `MoveSettleWatcher` (what happens after, until it stops changing) |
| `Feedback/` | `MoveIndicator` - the outline drawn where the window landed |
| `Tray/` | `SystemTrayIcon`, `UserSettings`, `StartupRegistry` |
| `Win32/` | `NativeMethods` - every P/Invoke in the app |
| `Diagnostics/` | `DebugLog` - written to `%TEMP%\windowmover-debug.log` |

A move reads top to bottom: `MouseGestureHook` sees the gesture and asks
`ButtonComboTracker` (in `WindowMover.Core`) what it means, `WindowMove` carries it
out, and `MoveSettleWatcher` watches what the app does about it afterwards.

## Building and testing

Requires the .NET SDK. The app targets `net10.0-windows` and must be built on
Windows.

```
dotnet build
dotnet test                                                     # both suites
dotnet test WindowMover.Tests/WindowMover.Tests.csproj          # pure logic, quiet, always safe
dotnet test WindowMover.LiveTests/WindowMover.LiveTests.csproj
```

`WindowMover.LiveTests` drives the real move code against real windows:
it moves windows between your monitors, takes the foreground, and opens a
throwaway VS Code window (with its own temporary profile and extensions
directory, so it never touches the editor you have open). Run it
deliberately, not while you are working.

The live suite exists because the unit tests cannot see whether Windows
actually honoured a move. A maximized VS Code window silently refused to move
for as long as the app had existed, and no unit test could have caught it - a
plain test window, Notepad and Edge all move fine under the same broken code,
which is why one of these tests uses VS Code itself. Live tests are skipped
automatically on a single-monitor machine, and the VS Code one is skipped when
VS Code is not installed.

To produce the executable:

```
dotnet build WindowMover.App/WindowMover.App.csproj -c Release
```

It lands in `WindowMover.App/bin/Release/net10.0-windows/`.

## License

MIT
