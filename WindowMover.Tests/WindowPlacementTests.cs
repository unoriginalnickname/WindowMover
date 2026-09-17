using System.Drawing;
using WindowMover.Core;
using Xunit;

namespace WindowMover.Tests;

public class WindowPlacementTests
{
    private static readonly Size DefaultWindowSize = new(800, 600);

    [Fact]
    public void A_window_lands_centred_on_the_target_monitor()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);

        var plan = WindowPlacement.PlanMove(monitor, DefaultWindowSize);

        Assert.Equal(new Rectangle(560, 240, 800, 600), plan.TargetBounds);
    }

    [Fact]
    public void The_monitors_own_position_is_taken_into_account()
    {
        // The second monitor starts at x=1920 in virtual-desktop coordinates, so the window
        // has to be offset by that much or it lands back on the first monitor.
        var monitor = new Rectangle(1920, 0, 1920, 1080);

        var plan = WindowPlacement.PlanMove(monitor, DefaultWindowSize);

        Assert.Equal(new Rectangle(2480, 240, 800, 600), plan.TargetBounds);
    }

    [Fact]
    public void A_monitor_above_or_left_of_the_primary_gets_negative_coordinates()
    {
        var monitor = new Rectangle(-1280, -1024, 1280, 1024);

        var plan = WindowPlacement.PlanMove(monitor, DefaultWindowSize);

        Assert.Equal(new Rectangle(-1040, -812, 800, 600), plan.TargetBounds);
    }

    [Fact]
    public void A_window_bigger_than_the_monitor_hangs_off_both_edges_evenly()
    {
        // The user can set any window size from the tray menu, including one larger than
        // the monitor. Centring then means overhanging equally on both sides.
        var monitor = new Rectangle(0, 0, 1024, 768);

        var plan = WindowPlacement.PlanMove(monitor, new Size(1600, 1200));

        Assert.Equal(new Rectangle(-288, -216, 1600, 1200), plan.TargetBounds);
    }

    [Fact]
    public void The_requested_size_is_used_as_is()
    {
        var plan = WindowPlacement.PlanMove(new Rectangle(0, 0, 1920, 1080), new Size(1280, 720));

        Assert.Equal(new Size(1280, 720), plan.TargetBounds.Size);
    }

    [Fact]
    public void Workspace_coordinates_match_screen_coordinates_when_the_primary_monitor_has_no_offset()
    {
        // The common case: taskbar on the bottom or right, so the primary monitor's work
        // area starts at (0, 0) same as its screen bounds.
        var screenRect = new Rectangle(1920, 0, 1920, 1040);

        var workspaceRect = WindowPlacement.ToWorkspaceCoordinates(screenRect, new Point(0, 0));

        Assert.Equal(screenRect, workspaceRect);
    }

    [Fact]
    public void Workspace_coordinates_subtract_the_primary_monitors_work_area_origin()
    {
        // Taskbar on the primary monitor's left edge pushes its work area's origin right by
        // the taskbar's width - every rectangle's coordinates shift by that same amount.
        var screenRect = new Rectangle(1920, 0, 1920, 1080);

        var workspaceRect = WindowPlacement.ToWorkspaceCoordinates(screenRect, new Point(48, 0));

        Assert.Equal(new Rectangle(1872, 0, 1920, 1080), workspaceRect);
    }

    [Fact]
    public void Workspace_coordinates_can_go_negative_for_a_monitor_left_of_the_primary()
    {
        var screenRect = new Rectangle(-1920, 0, 1920, 1080);

        var workspaceRect = WindowPlacement.ToWorkspaceCoordinates(screenRect, new Point(0, 0));

        Assert.Equal(new Rectangle(-1920, 0, 1920, 1080), workspaceRect);
    }

    [Fact]
    public void Matching_monitor_resolutions_keep_the_window_the_same_pixel_size()
    {
        var source = new Rectangle(0, 0, 1920, 1080);
        var target = new Rectangle(1920, 0, 1920, 1080);

        var size = WindowPlacement.ProportionalSize(source, new Size(960, 540), target);

        Assert.Equal(new Size(960, 540), size);
    }

    [Fact]
    public void Moving_to_a_smaller_monitor_shrinks_the_window_to_match_the_percentage()
    {
        // Half the width and height of a 1920x1080 monitor, moved to a 1280x720 monitor:
        // should land at half of 1280x720, not stay 960x540.
        var source = new Rectangle(0, 0, 1920, 1080);
        var target = new Rectangle(0, 0, 1280, 720);

        var size = WindowPlacement.ProportionalSize(source, new Size(960, 540), target);

        Assert.Equal(new Size(640, 360), size);
    }

    [Fact]
    public void Moving_to_a_bigger_monitor_grows_the_window_to_match_the_percentage()
    {
        var source = new Rectangle(0, 0, 1280, 720);
        var target = new Rectangle(0, 0, 1920, 1080);

        var size = WindowPlacement.ProportionalSize(source, new Size(640, 360), target);

        Assert.Equal(new Size(960, 540), size);
    }

    [Fact]
    public void Width_and_height_scale_independently_against_each_monitors_own_axis()
    {
        // An ultrawide (2560x1080) source next to a standard 1920x1080 target: the width
        // ratio and height ratio differ, so the window's own aspect ratio changes to match.
        var source = new Rectangle(0, 0, 2560, 1080);
        var target = new Rectangle(0, 0, 1920, 1080);

        // Half the ultrawide's width, a third of its height.
        var size = WindowPlacement.ProportionalSize(source, new Size(1280, 360), target);

        Assert.Equal(new Size(960, 360), size);
    }

    [Fact]
    public void A_full_screen_sized_window_stays_full_screen_sized_on_the_new_monitor()
    {
        var source = new Rectangle(0, 0, 1920, 1080);
        var target = new Rectangle(0, 0, 3840, 2160);

        var size = WindowPlacement.ProportionalSize(source, new Size(1920, 1080), target);

        Assert.Equal(new Size(3840, 2160), size);
    }
}
