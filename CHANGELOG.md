# Changelog

What changed between releases, newest first. Bugs and their history are in ISSUES.md.

## Unreleased

### Added

- **Resize with Ctrl+Shift + scroll.** Scroll up to make the focused window bigger
  and down to make it smaller, by 10% a notch, around its own centre. It stays on
  its monitor and never grows past the screen. Scrolling down on a maximized window
  takes it out of maximized and shrinks it. While you resize, the scroll is held
  back from the app, so the page doesn't scroll or zoom at the same time.
- **Size after a move**, a new tray menu option. Instead of keeping its share of
  the screen, a moved window can land at three-quarters, two-thirds, half or a
  third of the new screen, centred on where it would have gone. Maximized windows
  still land maximized. The choice is remembered across restarts.
- **Half the screen with Ctrl+Shift + Mouse4 or Mouse5.** The focused window
  fills the left (Mouse4) or right (Mouse5) half of its monitor, edge to edge
  and full height. The button press is held back from the app, so a browser
  doesn't go back or forward at the same time.
- The About box lists the resize and half-screen controls.

## 1.0 - 2026-10-01

The first release: pull a window to the monitor under the cursor (Mouse4 +
middle-click) or cycle it to the next monitor (Mouse5 + middle-click), keeping its
place and share of the screen, with the tray toggles for starting with Windows,
hiding the window while it moves and showing where it landed.
