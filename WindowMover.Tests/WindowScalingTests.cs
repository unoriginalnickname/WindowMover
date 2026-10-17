using System.Drawing;
using WindowMover.Core;
using Xunit;

namespace WindowMover.Tests;

// Ctrl+Shift + wheel: how a window's bounds change for a given wheel delta on a given screen.
public class WindowScalingTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);
    private const int NotchUp = WindowScaling.WheelDeltaPerNotch;
    private const int NotchDown = -WindowScaling.WheelDeltaPerNotch;

    [Fact]
    public void A_notch_up_grows_the_window_by_ten_percent_around_its_centre()
    {
        var bounds = WindowScaling.Scaled(new Rectangle(500, 300, 800, 400), Screen, NotchUp);

        Assert.Equal(new Rectangle(460, 280, 880, 440), bounds);
    }

    [Fact]
    public void A_notch_down_shrinks_it_around_its_centre()
    {
        var bounds = WindowScaling.Scaled(new Rectangle(500, 300, 880, 440), Screen, NotchDown);

        Assert.Equal(new Rectangle(540, 320, 800, 400), bounds);
    }

    [Fact]
    public void A_fraction_of_a_notch_from_a_smooth_wheel_scales_by_a_fraction_of_a_step()
    {
        var bounds = WindowScaling.Scaled(new Rectangle(500, 300, 800, 400), Screen, NotchUp / 4);

        Assert.InRange(bounds.Width, 801, 879);
    }

    [Fact]
    public void It_never_grows_past_the_screen_and_stays_fully_on_it()
    {
        var bounds = WindowScaling.Scaled(new Rectangle(100, 50, 1800, 1000), Screen, NotchUp);

        Assert.Equal(Screen, bounds);
    }

    [Fact]
    public void Growing_near_an_edge_pushes_the_window_back_onto_the_screen()
    {
        var bounds = WindowScaling.Scaled(new Rectangle(1100, 0, 800, 400), Screen, NotchUp);

        Assert.Equal(new Rectangle(1040, 0, 880, 440), bounds);
    }

    [Fact]
    public void Once_one_side_reaches_the_screen_only_the_other_keeps_growing()
    {
        var bounds = WindowScaling.Scaled(new Rectangle(0, 100, 1920, 600), Screen, NotchUp);

        Assert.Equal(1920, bounds.Width);
        Assert.Equal(660, bounds.Height);
    }

    [Fact]
    public void It_stops_shrinking_at_the_minimum_size()
    {
        var bounds = WindowScaling.Scaled(new Rectangle(500, 300, 330, 205), Screen, NotchDown);

        Assert.Equal(WindowScaling.MinimumSize, bounds.Size);
    }

    [Fact]
    public void A_window_already_under_the_minimum_is_not_grown_by_shrinking_it()
    {
        var bounds = WindowScaling.Scaled(new Rectangle(500, 300, 200, 100), Screen, NotchDown);

        Assert.Equal(new Size(200, 100), bounds.Size);
    }

    [Fact]
    public void The_screens_own_position_is_taken_into_account()
    {
        var second = new Rectangle(1920, 0, 1920, 1080);

        var bounds = WindowScaling.Scaled(new Rectangle(3500, 0, 800, 400), second, NotchUp);

        Assert.Equal(new Rectangle(2960, 0, 880, 440), bounds);
    }
}
