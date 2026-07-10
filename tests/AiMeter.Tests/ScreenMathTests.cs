using System;
using AiMeter.Windowing;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class ScreenMathTests
{
    // A 1920x1080 work area with origin at (0,0) in the common (100% DPI) case.
    private const double Left = 0;
    private const double Top = 0;
    private const double Width = 1920;
    private const double Height = 1080;

    [Fact]
    public void ClampToBounds_leaves_a_fully_on_screen_position_unchanged()
    {
        var (left, top) = ScreenMath.ClampToBounds(100, 100, 200, 200, Left, Top, Width, Height);

        left.Should().Be(100);
        top.Should().Be(100);
    }

    [Fact]
    public void ClampToBounds_pulls_a_window_back_that_exceeds_the_right_edge()
    {
        var (left, top) = ScreenMath.ClampToBounds(1900, 400, 200, 200, Left, Top, Width, Height);

        left.Should().Be(Width - 200, "the window's right edge must not leave the work area");
        top.Should().Be(400);
    }

    [Fact]
    public void ClampToBounds_pulls_a_window_back_that_exceeds_the_bottom_edge()
    {
        var (left, top) = ScreenMath.ClampToBounds(400, 1000, 200, 200, Left, Top, Width, Height);

        left.Should().Be(400);
        top.Should().Be(Height - 200, "the window's bottom edge must not leave the work area");
    }

    [Fact]
    public void ClampToBounds_pulls_a_window_back_above_and_left_of_the_work_area()
    {
        var (left, top) = ScreenMath.ClampToBounds(-50, -50, 200, 200, Left, Top, Width, Height);

        left.Should().Be(Left);
        top.Should().Be(Top);
    }

    [Fact]
    public void ClampToBounds_pins_top_left_to_origin_when_window_is_larger_than_the_work_area()
    {
        var (left, top) = ScreenMath.ClampToBounds(50, 50, 3000, 3000, Left, Top, Width, Height);

        left.Should().Be(Left, "keep the top-left corner visible when the widget can't fully fit");
        top.Should().Be(Top);
    }

    [Fact]
    public void ClampToBounds_clamps_relative_to_a_secondary_monitor_whose_origin_is_not_zero()
    {
        // Secondary monitor to the right of the primary, e.g. origin (1920, 0).
        var (left, top) = ScreenMath.ClampToBounds(3700, 1000, 200, 200, 1920, 0, 1920, 1080);

        left.Should().Be(1920 + 1920 - 200, "clamp against the window's own monitor, not the primary");
        top.Should().Be(1080 - 200);
    }
}