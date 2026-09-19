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

        var bounds = WindowPlacement.Centered(monitor, DefaultWindowSize);

        Assert.Equal(new Rectangle(560, 240, 800, 600), bounds);
    }

    [Fact]
    public void The_monitors_own_position_is_taken_into_account_when_centring()
    {
        // The second monitor starts at x=1920 in virtual-desktop coordinates, so the window
        // has to be offset by that much or it lands back on the first monitor.
        var monitor = new Rectangle(1920, 0, 1920, 1080);

        var bounds = WindowPlacement.Centered(monitor, DefaultWindowSize);

        Assert.Equal(new Rectangle(2480, 240, 800, 600), bounds);
    }

    [Fact]
    public void A_monitor_above_or_left_of_the_primary_gets_negative_centred_coordinates()
    {
        var monitor = new Rectangle(-1280, -1024, 1280, 1024);

        var bounds = WindowPlacement.Centered(monitor, DefaultWindowSize);

        Assert.Equal(new Rectangle(-1040, -812, 800, 600), bounds);
    }

    [Fact]
    public void Centring_a_window_bigger_than_the_monitor_hangs_off_both_edges_evenly()
    {
        var monitor = new Rectangle(0, 0, 1024, 768);

        var bounds = WindowPlacement.Centered(monitor, new Size(1600, 1200));

        Assert.Equal(new Rectangle(-288, -216, 1600, 1200), bounds);
    }

    [Fact]
    public void A_window_keeps_the_same_relative_position_on_the_target_monitor()
    {
        // 10% in from the left, 20% down from the top of a 1920x1080 source monitor.
        var source = new Rectangle(0, 0, 1920, 1080);
        var currentBounds = new Rectangle(192, 216, 800, 600);
        // A same-sized target far bigger than the window, so clamping never kicks in -
        // isolates the relative-position math on its own.
        var target = new Rectangle(0, 0, 3840, 2160);

        var bounds = WindowPlacement.ProportionalPosition(source, currentBounds, target, new Size(800, 600));

        // Same 10%/20% offsets, scaled onto the target monitor's own size.
        Assert.Equal(new Rectangle(384, 432, 800, 600), bounds);
    }

    [Fact]
    public void The_target_monitors_own_position_is_taken_into_account()
    {
        var source = new Rectangle(0, 0, 1920, 1080);
        var currentBounds = new Rectangle(192, 216, 800, 600); // 10%/20% in
        var target = new Rectangle(3840, 0, 3840, 2160); // second monitor, offset in virtual desktop

        var bounds = WindowPlacement.ProportionalPosition(source, currentBounds, target, new Size(800, 600));

        Assert.Equal(new Rectangle(3840 + 384, 432, 800, 600), bounds);
    }

    [Fact]
    public void A_position_that_would_land_off_the_right_or_bottom_edge_is_pulled_back_onto_the_monitor()
    {
        // The window sat near the bottom-right of a big source monitor - reproducing that
        // offset on a smaller target, combined with its own size, would push it half off
        // the right and bottom edges. It should be pulled back fully onto the monitor.
        var source = new Rectangle(0, 0, 2560, 1440);
        var currentBounds = new Rectangle(2000, 1200, 500, 200);
        var target = new Rectangle(0, 0, 1920, 1080);

        var bounds = WindowPlacement.ProportionalPosition(source, currentBounds, target, new Size(600, 300));

        // Clamped to the monitor's own right/bottom edge: 1920 - 600 = 1320, 1080 - 300 = 780.
        Assert.Equal(new Rectangle(1320, 780, 600, 300), bounds);
    }

    [Fact]
    public void An_oversized_moved_window_does_not_get_pushed_off_the_top_by_a_naive_centre()
    {
        // Reproduces the reported bug: a window sitting near the top of a 1440p/125% monitor,
        // whose DPI-compensated size ends up taller than the smaller 1080p target - centring
        // that (the old behaviour) pushes the title bar off the top of the screen. Anchoring
        // to the top instead keeps the title bar - and its controls - reachable, even though
        // the bottom of the window now hangs off screen instead.
        var source = new Rectangle(0, 0, 2560, 1440);
        var currentBounds = new Rectangle(200, 40, 1400, 1000); // sitting close to the top
        var target = new Rectangle(0, 0, 1920, 1080);
        var oversizedTargetSize = new Size(1400, 1150); // taller than the 1080-tall target

        var bounds = WindowPlacement.ProportionalPosition(source, currentBounds, target, oversizedTargetSize);

        Assert.Equal(0, bounds.Y); // pinned to the top, never negative
        Assert.Equal(1150, bounds.Height);
    }

    [Fact]
    public void A_window_too_wide_for_the_monitor_hangs_off_both_side_edges_evenly()
    {
        var source = new Rectangle(0, 0, 1920, 1080);
        var currentBounds = new Rectangle(0, 0, 1920, 1080); // fills its own monitor
        var target = new Rectangle(0, 0, 1024, 768);

        var bounds = WindowPlacement.ProportionalPosition(source, currentBounds, target, new Size(1600, 300));

        // Same as Centered on the X axis: negative half the overhang each side.
        Assert.Equal(-288, bounds.X);
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

    [Fact]
    public void A_size_already_within_the_monitor_is_left_alone()
    {
        var size = WindowPlacement.ClampToMonitor(new Size(800, 600), new Rectangle(0, 0, 1920, 1080));

        Assert.Equal(new Size(800, 600), size);
    }

    [Fact]
    public void An_oversized_width_is_clamped_to_the_monitors_width()
    {
        // Reproduces the reported bug: a large window on a 1440p/125% monitor, DPI-
        // compensated (inflated ~1.25x) for a smaller 1080p/100% target, ends up wider than
        // the target monitor itself.
        var size = WindowPlacement.ClampToMonitor(new Size(2160, 900), new Rectangle(0, 0, 1920, 1080));

        Assert.Equal(1920, size.Width);
        Assert.Equal(900, size.Height); // untouched - only the oversized axis is clamped
    }

    [Fact]
    public void An_oversized_height_is_clamped_to_the_monitors_height()
    {
        var size = WindowPlacement.ClampToMonitor(new Size(1400, 1215), new Rectangle(0, 0, 1920, 1080));

        Assert.Equal(1400, size.Width);
        Assert.Equal(1080, size.Height);
    }

    [Fact]
    public void A_size_exactly_matching_the_monitor_is_left_alone()
    {
        var size = WindowPlacement.ClampToMonitor(new Size(1920, 1080), new Rectangle(0, 0, 1920, 1080));

        Assert.Equal(new Size(1920, 1080), size);
    }

    [Fact]
    public void Target_size_is_the_clamped_proportional_size_with_no_dpi_math_involved()
    {
        // See ISSUES.md's resolved DPI-sizing history: DetermineTargetSize used to also bet-
        // inflate by a DPI ratio, betting a per-monitor-DPI-aware app would shrink it back on
        // its own. That's retired - self-resizing apps are now handled by reactively
        // correcting whatever they change it to (Program.cs), not by pre-guessing a value for
        // them, so this is just ProportionalSize clamped to the target monitor.
        var source = new Rectangle(0, 0, 2560, 1440); // 1440p working area
        var target = new Rectangle(0, 0, 1920, 1080); // 1080p working area

        var size = WindowPlacement.DetermineTargetSize(source, new Size(1280, 1440), target);

        Assert.Equal(new Size(960, 1080), size);
    }

    [Fact]
    public void Target_size_never_exceeds_the_target_monitor_even_with_no_headroom()
    {
        // A window already taller than its own source monitor's working area, moved onto a
        // shorter target monitor, must still be clamped - ClampToMonitor's own job, exercised
        // through the composed function. (A height of 1440 here would be a no-op regardless of
        // whether clamping ran at all, since 1440/1440 scaled onto a 1080-tall target lands
        // exactly on 1080 either way - 1600 makes the pre-clamp value actually overshoot to
        // 1200, so this only passes if ClampToMonitor is really being applied.)
        var source = new Rectangle(0, 0, 2560, 1440); // 1440p working area
        var target = new Rectangle(0, 0, 1920, 1080); // 1080p working area

        var size = WindowPlacement.DetermineTargetSize(source, new Size(1440, 1600), target);

        Assert.Equal(1080, size.Height);
    }
}
