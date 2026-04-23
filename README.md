Window Mover

A lightweight Windows system tray utility that lets you quickly move windows between monitors using your mouse side buttons.

Features

Move any window to the next monitor with a simple button combo
Move a window directly to whichever monitor your cursor is on
Runs silently in the system tray — no taskbar clutter
Optional "Start with Windows" support via the registry
Configurable default window size for moved windows
Smart filtering: skips the taskbar, desktop, tooltips, and other system UI

Controls
ShortcutActionMouse4 + Middle ClickCycle window to the next monitorMouse5 + Middle ClickCycle window to the next monitorMouse4 + Mouse5 + Middle ClickMove window to the monitor your cursor is on

Mouse4 = the back thumb button, Mouse5 = the forward thumb button (on most mice)

Installation

Download or build the executable (see Building below)
Run WindowMover.exe — it will appear in your system tray
Optionally enable Start with Windows via the tray icon's right-click menu

Building Requirements

Visual Studio 2019 or later
.NET / Windows Forms project targeting Windows

Steps

Clone the repository
Open the solution in Visual Studio
Build in Release mode (Ctrl+Shift+B)
The executable will be in bin/Release/

Usage

Right-click the system tray icon to access options:

Start with Windows — toggle auto-launch on login
Set Window Size — customize the width and height of the windows that are resized when moved (default: 800×600)
About — view controls and current settings
Exit — close the application

How It Works

Window Mover installs a low-level mouse hook (WH_MOUSE_LL) that intercepts Mouse4/Mouse5 button events system-wide. When a side button is held, and the middle click is pressed, it moves the foreground window to the target monitor, restoring it first if maximized and re-maximizing it afterward.
Windows that cannot be moved are automatically skipped: the taskbar, invisible windows, tool windows, very small UI elements, and desktop icons.
Notes

Only one instance can run at a time (enforced with a named mutex)
Window size set via the tray menu applies to all subsequent moves in that session; it resets to 800×600 on restart
Requires Windows (uses Win32 API via P/Invoke)

License
MIT
