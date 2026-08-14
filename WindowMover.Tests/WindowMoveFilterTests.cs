using System.Drawing;
using WindowMover.Core;
using Xunit;

namespace WindowMover.Tests;

public class WindowMoveFilterTests
{
    // An ordinary app window: visible, no special styles, a sensible size, owned by nobody.
    private static WindowSnapshot NormalWindow(
        bool isTaskbar = false,
        bool isVisible = true,
        uint extendedStyle = 0,
        Rectangle? bounds = null,
        bool isDesktopChild = false) =>
        new(isTaskbar, isVisible, extendedStyle, bounds ?? new Rectangle(100, 100, 800, 600), isDesktopChild);

    [Fact]
    public void An_ordinary_app_window_can_be_moved()
    {
        Assert.True(WindowMoveFilter.IsSafeToMove(NormalWindow()));
    }

    [Fact]
    public void The_taskbar_is_left_alone()
    {
        Assert.False(WindowMoveFilter.IsSafeToMove(NormalWindow(isTaskbar: true)));
    }

    [Fact]
    public void Tool_windows_are_left_alone()
    {
        var tooltip = NormalWindow(extendedStyle: WindowMoveFilter.WS_EX_TOOLWINDOW);

        Assert.False(WindowMoveFilter.IsSafeToMove(tooltip));
    }

    [Fact]
    public void The_tool_window_flag_is_tested_as_a_bit_not_as_the_whole_style()
    {
        // Real windows carry several style bits at once (here: WS_EX_TOPMOST | WS_EX_LAYERED
        // alongside WS_EX_TOOLWINDOW). Comparing the whole value instead of masking the one
        // bit would let every real tool window through.
        var floatingPalette = NormalWindow(extendedStyle: 0x00000008 | 0x00080000 | WindowMoveFilter.WS_EX_TOOLWINDOW);

        Assert.False(WindowMoveFilter.IsSafeToMove(floatingPalette));
    }

    [Fact]
    public void Other_style_bits_on_their_own_do_not_block_a_move()
    {
        // WS_EX_TOPMOST - an always-on-top window is still a window the user may want moved.
        Assert.True(WindowMoveFilter.IsSafeToMove(NormalWindow(extendedStyle: 0x00000008)));
    }

    [Fact]
    public void Invisible_windows_are_left_alone()
    {
        Assert.False(WindowMoveFilter.IsSafeToMove(NormalWindow(isVisible: false)));
    }

    [Fact]
    public void The_desktops_own_icon_layer_is_left_alone()
    {
        Assert.False(WindowMoveFilter.IsSafeToMove(NormalWindow(isDesktopChild: true)));
    }

    [Theory]
    [InlineData(20, 600)]  // too narrow
    [InlineData(800, 20)]  // too short
    [InlineData(20, 20)]   // both
    public void Tiny_windows_are_treated_as_system_UI_and_left_alone(int width, int height)
    {
        var tiny = NormalWindow(bounds: new Rectangle(0, 0, width, height));

        Assert.False(WindowMoveFilter.IsSafeToMove(tiny));
    }

    [Fact]
    public void A_window_exactly_at_the_size_cutoff_is_still_movable()
    {
        var justBigEnough = NormalWindow(bounds: new Rectangle(0, 0,
            WindowMoveFilter.MinimumMovableSize, WindowMoveFilter.MinimumMovableSize));

        Assert.True(WindowMoveFilter.IsSafeToMove(justBigEnough));
    }

    [Fact]
    public void A_window_whose_size_Windows_will_not_report_is_still_movable()
    {
        // GetWindowRect can fail; the size check is skipped in that case rather than
        // refusing the window outright.
        var unmeasurable = new WindowSnapshot(
            IsTaskbar: false, IsVisible: true, ExtendedStyle: 0, Bounds: null, IsDesktopChild: false);

        Assert.True(WindowMoveFilter.IsSafeToMove(unmeasurable));
    }
}
