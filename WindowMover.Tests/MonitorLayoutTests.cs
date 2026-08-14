using System.Drawing;
using WindowMover.Core;
using Xunit;

namespace WindowMover.Tests;

public class MonitorLayoutTests
{
    // Three 1080p monitors in a row, the way Windows would report them: the primary at the
    // origin and the others extending to the right.
    private static MonitorLayout ThreeInARow() => new(new[]
    {
        new Rectangle(0, 0, 1920, 1080),
        new Rectangle(1920, 0, 1920, 1080),
        new Rectangle(3840, 0, 1920, 1080)
    });

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)] // the whole point of "cycling": the last monitor wraps to the first
    public void Cycling_walks_along_the_monitors_and_wraps_around(int current, int expectedNext)
    {
        Assert.True(ThreeInARow().TryGetNextMonitorIndex(current, out int next));
        Assert.Equal(expectedNext, next);
    }

    [Fact]
    public void Two_monitors_just_swap_back_and_forth()
    {
        var layout = new MonitorLayout(new[]
        {
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(1920, 0, 1920, 1080)
        });

        Assert.True(layout.TryGetNextMonitorIndex(0, out int fromFirst));
        Assert.True(layout.TryGetNextMonitorIndex(1, out int fromSecond));
        Assert.Equal(1, fromFirst);
        Assert.Equal(0, fromSecond);
    }

    [Fact]
    public void A_single_monitor_has_nowhere_to_cycle_to()
    {
        var layout = new MonitorLayout(new[] { new Rectangle(0, 0, 1920, 1080) });

        Assert.False(layout.TryGetNextMonitorIndex(0, out _));
    }

    [Fact]
    public void A_window_sitting_on_one_monitor_is_found_there()
    {
        var window = new Rectangle(2000, 100, 800, 600);

        Assert.Equal(1, ThreeInARow().IndexOfMonitorShowing(window));
    }

    [Fact]
    public void A_window_straddling_two_monitors_belongs_to_the_one_showing_more_of_it()
    {
        // 800 wide, starting 200px before the second monitor: 200px on the first, 600 on
        // the second.
        var window = new Rectangle(1720, 100, 800, 600);

        Assert.Equal(1, ThreeInARow().IndexOfMonitorShowing(window));
    }

    [Fact]
    public void Straddling_the_other_way_picks_the_other_monitor()
    {
        // Same straddle, mirrored: 600px on the first monitor, 200 on the second.
        var window = new Rectangle(1320, 100, 800, 600);

        Assert.Equal(0, ThreeInARow().IndexOfMonitorShowing(window));
    }

    [Fact]
    public void A_window_that_is_completely_off_screen_falls_back_to_the_nearest_monitor()
    {
        // Parked above the third monitor - happens when a monitor is unplugged or an app
        // restores a stale saved position. It must still resolve to a monitor, otherwise
        // there is nothing to cycle from.
        var window = new Rectangle(4000, -3000, 800, 600);

        Assert.Equal(2, ThreeInARow().IndexOfMonitorShowing(window));
    }

    [Fact]
    public void An_off_screen_window_can_still_be_cycled()
    {
        var layout = ThreeInARow();
        int current = layout.IndexOfMonitorShowing(new Rectangle(-5000, 0, 800, 600));

        Assert.True(layout.TryGetNextMonitorIndex(current, out int next));
        Assert.Equal(1, next); // nearest is the leftmost monitor, so next is the middle one
    }

    [Fact]
    public void Monitors_at_negative_coordinates_work_like_any_other()
    {
        // A secondary monitor placed to the left of the primary gets negative coordinates.
        var layout = new MonitorLayout(new[]
        {
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(-1280, 0, 1280, 1024)
        });

        Assert.Equal(1, layout.IndexOfMonitorShowing(new Rectangle(-1000, 50, 800, 600)));
    }

    [Fact]
    public void No_monitors_at_all_resolves_to_nothing_rather_than_throwing()
    {
        var layout = new MonitorLayout(Array.Empty<Rectangle>());

        Assert.Equal(-1, layout.IndexOfMonitorShowing(new Rectangle(0, 0, 800, 600)));
        Assert.False(layout.TryGetNextMonitorIndex(-1, out _));
    }
}
