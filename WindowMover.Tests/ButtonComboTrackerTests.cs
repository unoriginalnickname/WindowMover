using WindowMover.Core;
using Xunit;

namespace WindowMover.Tests;

// The combo is: hold a side button (which captures the window under the mouse), then
// middle click. These tests drive the tracker with button sequences and check which of
// the three outcomes falls out.
public class ButtonComboTrackerTests
{
    // Stand-ins for real window handles. The tracker only ever passes them back out.
    private static readonly nint SomeWindow = 0x1234;
    private static readonly nint AnotherWindow = 0x5678;
    private static readonly nint NoWindow = 0;

    [Fact]
    public void Mouse4_then_middle_click_cycles_to_the_next_monitor()
    {
        var tracker = new ButtonComboTracker();

        tracker.SideButtonDown(SideButton.Mouse4, SomeWindow);
        var request = tracker.MiddleButtonDown();

        Assert.Equal(MoveCommand.NextMonitor, request.Command);
        Assert.Equal(SomeWindow, request.Window);
    }

    [Fact]
    public void Mouse5_behaves_the_same_as_Mouse4_on_its_own()
    {
        var tracker = new ButtonComboTracker();

        tracker.SideButtonDown(SideButton.Mouse5, SomeWindow);

        Assert.Equal(MoveCommand.NextMonitor, tracker.MiddleButtonDown().Command);
    }

    [Theory]
    [InlineData(SideButton.Mouse4, SideButton.Mouse5)]
    [InlineData(SideButton.Mouse5, SideButton.Mouse4)]
    public void Holding_both_side_buttons_beats_holding_either_one(SideButton first, SideButton second)
    {
        var tracker = new ButtonComboTracker();

        tracker.SideButtonDown(first, SomeWindow);
        tracker.SideButtonDown(second, SomeWindow);

        Assert.Equal(MoveCommand.CursorMonitor, tracker.MiddleButtonDown().Command);
    }

    [Fact]
    public void Releasing_one_of_two_side_buttons_falls_back_to_the_single_button_action()
    {
        var tracker = new ButtonComboTracker();

        tracker.SideButtonDown(SideButton.Mouse4, SomeWindow);
        tracker.SideButtonDown(SideButton.Mouse5, SomeWindow);
        tracker.SideButtonUp(SideButton.Mouse5);

        Assert.Equal(MoveCommand.NextMonitor, tracker.MiddleButtonDown().Command);
    }

    [Fact]
    public void Middle_click_after_the_side_button_is_released_does_nothing()
    {
        var tracker = new ButtonComboTracker();

        tracker.SideButtonDown(SideButton.Mouse4, SomeWindow);
        tracker.SideButtonUp(SideButton.Mouse4);
        var request = tracker.MiddleButtonDown();

        Assert.Equal(MoveCommand.None, request.Command);
        Assert.Equal(NoWindow, request.Window);
    }

    [Fact]
    public void Middle_click_on_its_own_does_nothing()
    {
        var tracker = new ButtonComboTracker();

        Assert.Equal(MoveCommand.None, tracker.MiddleButtonDown().Command);
    }

    [Fact]
    public void A_window_we_refuse_to_move_captures_nothing_so_the_combo_does_nothing()
    {
        var tracker = new ButtonComboTracker();

        // The caller passes 0 when the foreground window failed the move filter.
        tracker.SideButtonDown(SideButton.Mouse4, NoWindow);

        Assert.Equal(MoveCommand.None, tracker.MiddleButtonDown().Command);
    }

    [Fact]
    public void Pressing_the_second_side_button_recaptures_whatever_is_now_in_front()
    {
        var tracker = new ButtonComboTracker();

        tracker.SideButtonDown(SideButton.Mouse4, SomeWindow);
        tracker.SideButtonDown(SideButton.Mouse5, AnotherWindow);

        Assert.Equal(AnotherWindow, tracker.MiddleButtonDown().Window);
    }

    [Fact]
    public void Repeated_middle_clicks_keep_working_while_the_side_button_is_held()
    {
        var tracker = new ButtonComboTracker();

        tracker.SideButtonDown(SideButton.Mouse4, SomeWindow);
        tracker.MiddleButtonDown();

        // Moving a window twice in a row should walk it across two monitors, not stall.
        Assert.Equal(MoveCommand.NextMonitor, tracker.MiddleButtonDown().Command);
    }

    [Fact]
    public void The_captured_window_survives_releasing_only_one_of_the_two_buttons()
    {
        var tracker = new ButtonComboTracker();

        tracker.SideButtonDown(SideButton.Mouse4, SomeWindow);
        tracker.SideButtonDown(SideButton.Mouse5, SomeWindow);
        tracker.SideButtonUp(SideButton.Mouse4);

        Assert.Equal(SomeWindow, tracker.CapturedWindow);

        tracker.SideButtonUp(SideButton.Mouse5);

        Assert.Equal(NoWindow, tracker.CapturedWindow);
    }
}
