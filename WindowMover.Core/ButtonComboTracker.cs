namespace WindowMover.Core;

// The two extra buttons on the side of the mouse.
// Mouse4 = the back thumb button, Mouse5 = the forward thumb button (on most mice).
public enum SideButton
{
    Mouse4,
    Mouse5
}

// What the user asked for by pressing middle click while holding side button(s).
public enum MoveCommand
{
    None,           // nothing to do (no window captured, or no side button held)
    NextMonitor,    // one side button held: cycle the window to the next monitor
    CursorMonitor   // both side buttons held: send the window to the monitor the cursor is on
}

// A command plus the window it applies to. The window is the raw Win32 handle,
// carried through untouched - the core never calls into Win32 with it.
public readonly record struct MoveRequest(MoveCommand Command, nint Window);

// Tracks which side buttons are currently held and which window was captured when
// the first one went down.
//
// The window is captured on button-down rather than on the middle click because by the
// time the user middle-clicks, the foreground window may have changed (clicking can
// focus something else). The captured window is what the user was looking at when they
// started the combo.
public sealed class ButtonComboTracker
{
    private bool isMouse4Held;
    private bool isMouse5Held;

    // The window the move will apply to, or 0 when nothing usable is captured.
    public nint CapturedWindow { get; private set; }

    public bool IsHeld(SideButton button) =>
        button == SideButton.Mouse4 ? isMouse4Held : isMouse5Held;

    // A side button went down. The caller passes the window to capture, or 0 if the
    // window under the mouse is one we refuse to move (see WindowMoveFilter).
    // A later press replaces an earlier capture, including replacing it with 0 - if the
    // user re-presses over the taskbar, they should not still be dragging the old window.
    public void SideButtonDown(SideButton button, nint windowToCapture)
    {
        if (button == SideButton.Mouse4) isMouse4Held = true;
        else isMouse5Held = true;

        CapturedWindow = windowToCapture;
    }

    public void SideButtonUp(SideButton button)
    {
        if (button == SideButton.Mouse4) isMouse4Held = false;
        else isMouse5Held = false;

        // Once the user has let go of both side buttons the combo is over, so forget the
        // window. A middle click after that is just an ordinary middle click.
        if (!isMouse4Held && !isMouse5Held) CapturedWindow = 0;
    }

    // Middle click: decide what the held buttons mean. Holding both beats holding either
    // one, so the both-buttons check comes first.
    public MoveRequest MiddleButtonDown()
    {
        if (CapturedWindow == 0) return new MoveRequest(MoveCommand.None, 0);

        if (isMouse4Held && isMouse5Held) return new MoveRequest(MoveCommand.CursorMonitor, CapturedWindow);
        if (isMouse4Held || isMouse5Held) return new MoveRequest(MoveCommand.NextMonitor, CapturedWindow);

        return new MoveRequest(MoveCommand.None, 0);
    }
}
