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

Building and testing

Requires the .NET SDK (the app itself targets net10.0-windows, so building it needs Windows). From the repository root:

    dotnet build
    dotnet test

To produce the executable:

    dotnet build WindowMoverFinal/WindowMoverFinal.csproj -c Release

The executable ends up in WindowMoverFinal/bin/Release/net10.0-windows/.

You can also just open WindowMoverFinal.slnx in Visual Studio 2022 or later and build in Release mode (Ctrl+Shift+B).

Project layout

WindowMoverFinal — the Windows Forms app: P/Invoke declarations, the low-level mouse hook, the tray icon. A thin adapter over the core.
WindowMover.Core — the decision logic, targeting plain net8.0 with no Win32 or Windows Forms dependency: which windows may be moved, which monitor is next, where on that monitor a window lands, and what a button combo means.
WindowMover.Tests — xUnit tests for the core. They run without a desktop, so no monitors or windows are needed to run them.

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
