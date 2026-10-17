# WindowMover

A Windows system tray utility that moves windows between monitors using only the
mouse, and resizes them in place.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/pull-window-dark.svg">
  <img src="docs/pull-window-light.svg" alt="A mouse, four steps and two monitors. With the cursor on the left monitor, a blue window on the right monitor is pulled over by clicking its taskbar icon, holding Mouse4, middle-clicking and releasing Mouse4. Then the right monitor is switched off and the same steps pull a red window out of it.">
</picture>

## What it does

Use it when a window is on a monitor you've turned off, or when no keyboard is in
reach. It has two actions:

1. **Pull** - the window comes to the monitor your cursor is on.
2. **Cycle** - the window goes to the next monitor from the one it is on, in the
   order Windows lists them, wrapping back to the first after the last.

A moved window lands in the same spot and at the same size relative to the
screen: a window covering the right half of one monitor covers the right half of
the other, independent of the two monitors' resolutions and scaling settings. A
maximized window stays maximized.

Between monitors of different shapes, such as an ultrawide next to a standard
screen, a window gets stretched or squashed, because its width and height scale
separately. Moving it back restores its size.

If moved windows come out bigger than you want, pick a fixed size in the tray
menu's **Size after a move** instead: three-quarters, two-thirds, half or a third
of the screen. A moved window then lands at that size, centred on where it would
have gone. Maximized windows still land maximized.

### Resizing

Hold **Ctrl+Shift** and scroll: up makes the focused window bigger, down makes it
smaller, by 10% a notch, around its own centre. It stays on its monitor and never
grows past the screen; once one side reaches the edge, only the other keeps
growing. Scrolling down on a maximized window takes it out of maximized and
shrinks it from full screen.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/resize-window-dark.svg">
  <img src="docs/resize-window-light.svg" alt="A mouse with Ctrl and Shift keys above it, four steps and one monitor. With Ctrl and Shift held, the wheel scrolls up and the focused blue window grows a notch at a time until it reaches the top of the screen, then scrolls down and the window shrinks below its starting size, which a dashed outline marks. Then Ctrl and Shift are released.">
</picture>

While Ctrl+Shift is held and the focused window can be resized, the wheel does not
reach the app, so it neither scrolls nor zooms. Plain Shift is left alone because
most apps use Shift + scroll to scroll sideways. If you have more than one keyboard
layout installed and Windows switches layouts on Ctrl+Shift, turn that shortcut off
in Windows' input settings.

### Half the screen

Hold **Ctrl+Shift** and press **Mouse4** to make the focused window fill the left
half of its monitor, or **Mouse5** for the right half: full height, from the
screen's edge to its middle, with no gap at either. A maximized window comes out
of maximized to do it. The button press does not reach the app, so a browser does
not also go back or forward.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/snap-window-dark.svg">
  <img src="docs/snap-window-light.svg" alt="A mouse with Ctrl and Shift keys above it, four steps and one monitor. With Ctrl and Shift held, Mouse4 is pressed and the focused blue window fills the left half of the screen, then Mouse5 is pressed and it fills the right half. Then Ctrl and Shift are released.">
</picture>

## Controls

| Input | Role |
|---|---|
| Left-click | Puts a window in focus (for example, click its taskbar icon) |
| Hold Mouse4 | Selects pull |
| Hold Mouse5 | Selects cycle |
| Middle-click | Activates the selected action on the focused window |
| Hold Ctrl+Shift + scroll | Makes the focused window bigger (up) or smaller (down) |
| Hold Ctrl+Shift + Mouse4 / Mouse5 | Fills the left / right half of the screen with the focused window |

Mouse4 is the back thumb button and Mouse5 the forward one, on most mice. If both
are held, pull wins. The taskbar, desktop icons, tool windows and very small UI
elements are ignored.

## Running it

Download one of the two exes from
[Releases](https://github.com/unoriginalnickname/WindowMover/releases) and run it:

- **`WindowMover.exe`** (about 200 KB) needs the
  [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
  If it is missing, Windows says so when you start the exe and links to the
  download.
- **`WindowMover-standalone.exe`** (about 50 MB) has the runtime built in and
  needs nothing installed.

It appears in the system tray, and only one copy runs at a time.

The exe is not signed, so Windows SmartScreen may warn about it the first time.
Choose **More info**, then **Run anyway**.

## Tray menu

- **Start with Windows** - start it when you log in.
- **Hide window while it moves** - on by default. The window turns invisible
  until it has stopped moving, so you don't see it resize and re-maximize on the
  way. Turn it off if you'd rather WindowMover never changed how your windows
  are drawn.
- **Show where the window landed** - on by default. A brief outline with the
  monitor's number appears where the window landed, fading out over about half a
  second. Turn it off for a silent move.
- **Size after a move** - how big a moved window is on its new monitor. **Keep its
  share of the screen** (the default) is the behaviour described above; the others
  land every moved window at that fraction of the screen, whatever its size before.
- **About** - shows the controls.
- **Exit**

## How it works

A low-level mouse hook (`WH_MOUSE_LL`) sees every mouse button press system-wide.
When a side button goes down, WindowMover notes which window has focus. When the
middle button is clicked, it checks which side button is still held: Mouse4 means
pull and Mouse5 means cycle. If both side buttons were released, nothing moves.
A wheel turn while Ctrl and Shift are both down (read with `GetAsyncKeyState`)
resizes the focused window instead of scrolling, and a side button pressed with
them snaps it to half the screen. Most windows have an invisible resize border a
few pixels wide, so the snap reads the visible frame from DWM
(`DWMWA_EXTENDED_FRAME_BOUNDS`) and sizes the window so that is what meets the
edges. The resize is posted to the
window's own thread (`SWP_ASYNCWINDOWPOS`) rather than waited on, so an app that
is slow to repaint cannot hold up the mouse.

A maximized window ignores new bounds, so it is restored, moved and maximized
again, and Windows animates each step. Some apps resize themselves when they land
on a monitor with different scaling, overriding the size WindowMover just set;
`MoveSettleWatcher` watches for that (via `EVENT_OBJECT_LOCATIONCHANGE`, not
polling) and corrects it back. In both cases the window is made fully transparent
until it has stopped changing. That moment is watched for, not guessed with a
timer, because the maximize animation keeps running after the call that started
it returns. Transparency is used instead of `ShowWindow(SW_HIDE)` so the window
keeps its taskbar button, its place in the Z-order and its focus.

It does not bring the moved window to the front. Windows refuses that to a
background app while you're using another one (see ISSUES.md #6). The landing
outline is WindowMover's own window, so it can draw on top of anything without
taking focus or catching clicks.

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
| `Input/` | `MouseGestureHook` - the low-level mouse hook, turning button and wheel events into a move or a resize |
| `Moving/` | `MovableWindowCheck` (may this window be moved), `MoveTargetBounds` (where it lands), `WindowMove` (the move itself), `WindowResize` (Ctrl+Shift + wheel), `WindowSnap` (Ctrl+Shift + side button), `WindowTransparency` (hiding it on the way), `MoveSettleWatcher` (what happens after, until it stops changing) |
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
from the start, and no unit test could have caught it - a
plain test window, Notepad and Edge all move fine under the same broken code,
which is why one of these tests uses VS Code itself. Live tests are skipped
automatically on a single-monitor machine, and the VS Code one is skipped when
VS Code is not installed.

To produce the two exes that go on the Releases page:

```
dotnet publish WindowMover.App/WindowMover.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o publish/small
dotnet publish WindowMover.App/WindowMover.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish/standalone
```

Both come out as `WindowMover.App.exe`, in `publish/small/` and
`publish/standalone/`; they are renamed when uploaded.

## Changes

What each release added is in [CHANGELOG.md](CHANGELOG.md).

## License

MIT - see [LICENSE](LICENSE).
