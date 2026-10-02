# To do

Ideas that are wanted but not started. Bugs go in ISSUES.md, not here.

## Start menu shortcut, as a tray toggle

The app has no installer, so nothing in Start points to it. Add an "Add to Start menu"
toggle beside "Start with Windows":

- Off by default. On creates `%APPDATA%\Microsoft\Windows\Start Menu\Programs\WindowMover.lnk`
  (no admin needed); off deletes it.
- The tick shows whether a shortcut to *this* exe exists, the way the startup toggle does.
- If the exe is moved or replaced, repoint an existing shortcut on launch - the same idea as
  `StartupRegistry.RemoveStaleEntry`. Only ever repoint one, never create one unasked: run
  from a USB stick, a shortcut to `E:\WindowMover.exe` breaks the moment the stick is out.
- .NET has no shortcut API; needs a small `IShellLink` COM binding in `Win32/`.
- README: mention right-click in Start -> "Pin to taskbar", since an app cannot pin itself.
