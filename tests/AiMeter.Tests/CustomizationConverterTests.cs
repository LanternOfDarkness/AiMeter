using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AiMeter.Converters;
using AiMeter.Models;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class CustomizationConverterTests
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    private static UsageMetric Metric(double remainingPct, DateTime? reset = null, TimeSpan? window = null)
        => new()
        {
            TotalQuota = 100,
            RemainingQuota = remainingPct,
            ResetTime = reset,
            WindowDuration = window,
        };

    private static Color BarColor(UsageMetric metric, BarColorMode mode, DateTime now)
    {
        var conv = new MetricBarColorConverter();
        var brush = (SolidColorBrush)conv.Convert(new object[] { metric, mode, now }, typeof(Brush), null!, Ci);
        return brush.Color;
    }

    [Theory]
    [InlineData(50, 120, 60)]
    [InlineData(0, 200, 0)]
    [InlineData(100, 200, 200)]
    [InlineData(150, 100, 100)] // clamps above 100%
    public void PercentageWidthMulti_scales_and_clamps(double pct, double maxWidth, double expected)
    {
        var conv = new PercentageWidthMultiConverter();
        var result = conv.Convert(new object[] { pct, maxWidth }, typeof(double), null!, Ci);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(BarColorMode.Threshold)]
    [InlineData(BarColorMode.Progressive)]
    [InlineData(BarColorMode.Pace)]
    public void MetricBar_custom_hex_wins_in_every_mode(BarColorMode mode)
    {
        var metric = Metric(80, DateTime.Now.AddMinutes(30), TimeSpan.FromHours(1));
        metric.CustomColor = "#112233";

        BarColor(metric, mode, DateTime.Now).Should().Be((Color)ColorConverter.ConvertFromString("#112233"));
    }

    [Fact]
    public void MetricBar_malformed_custom_hex_falls_through_to_the_mode_color()
    {
        var metric = Metric(80);
        metric.CustomColor = "not-a-color";

        BarColor(metric, BarColorMode.Threshold, DateTime.Now).Should().Be(Color.FromRgb(46, 204, 113));
    }

    [Fact]
    public void MetricBar_threshold_mode_uses_the_quota_palette()
    {
        BarColor(Metric(80), BarColorMode.Threshold, DateTime.Now).Should().Be(Color.FromRgb(46, 204, 113), "high quota is green");
        BarColor(Metric(40), BarColorMode.Threshold, DateTime.Now).Should().Be(Color.FromRgb(241, 196, 15), "mid quota is yellow");
        BarColor(Metric(10), BarColorMode.Threshold, DateTime.Now).Should().Be(Color.FromRgb(231, 76, 60), "low quota is red");
    }

    [Fact]
    public void MetricBar_pace_without_duration_or_reset_falls_back_to_thresholds()
    {
        BarColor(Metric(80), BarColorMode.Pace, DateTime.Now).Should().Be(Color.FromRgb(46, 204, 113));
    }

    [Theory]
    [InlineData(0, 139, 0, 0)]
    [InlineData(10, 231, 76, 60)]
    [InlineData(25, 230, 126, 34)]
    [InlineData(50, 241, 196, 15)]
    [InlineData(75, 52, 152, 219)]
    [InlineData(100, 46, 204, 113)]
    public void MetricBar_progressive_hits_the_exact_stop_colors(int pct, int r, int g, int b)
    {
        BarColor(Metric(pct), BarColorMode.Progressive, DateTime.Now).Should().Be(Color.FromRgb((byte)r, (byte)g, (byte)b));
    }

    [Fact]
    public void MetricBar_progressive_interpolates_between_two_stops()
    {
        // 30% sits one-fifth of the way from the 25% stop toward the 50% stop.
        BarColor(Metric(30), BarColorMode.Progressive, DateTime.Now).Should().Be(Color.FromRgb(232, 140, 30));
    }

    [Fact]
    public void MetricBar_pace_is_green_blue_and_red_around_the_budget_anchors()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var reset = now.AddMinutes(30); // half of the 1h window already elapsed.
        var window = TimeSpan.FromHours(1);

        // Used 0% at the half-way point -> 50% under budget -> clamped green anchor.
        BarColor(Metric(100, reset, window), BarColorMode.Pace, now).Should().Be(Color.FromRgb(46, 204, 113), "well under budget is green");
        // Used exactly 50% at half-way -> delta 0 -> blue anchor.
        BarColor(Metric(50, reset, window), BarColorMode.Pace, now).Should().Be(Color.FromRgb(52, 152, 219), "exactly on pace is blue");
        // Used 90% at half-way -> 40% over budget -> clamped red anchor.
        BarColor(Metric(10, reset, window), BarColorMode.Pace, now).Should().Be(Color.FromRgb(231, 76, 60), "well over budget is red");
    }

    [Fact]
    public void GapToMargin_produces_the_requested_one_sided_thickness()
    {
        var conv = new GapToMarginConverter();

        conv.Convert(8d, typeof(Thickness), "Left", Ci).Should().Be(new Thickness(8, 0, 0, 0));
        conv.Convert(14d, typeof(Thickness), "Right", Ci).Should().Be(new Thickness(0, 0, 14, 0));
        conv.Convert(14d, typeof(Thickness), "NegativeRight", Ci).Should().Be(new Thickness(0, 0, -14, 0));
        // No parameter falls back to the left gap used by Compact row cells.
        conv.Convert(6d, typeof(Thickness), null!, Ci).Should().Be(new Thickness(6, 0, 0, 0));
    }

    [Fact]
    public void GapToMargin_keeps_vertical_components_zero_so_column_wrap_math_holds()
    {
        var conv = new GapToMarginConverter();

        foreach (var side in new[] { "Left", "Right", "NegativeRight" })
        {
            var t = (Thickness)conv.Convert(20d, typeof(Thickness), side, Ci);
            t.Top.Should().Be(0);
            t.Bottom.Should().Be(0);
        }
    }

    [Fact]
    public void FontScale_applies_factor_and_clamps_within_bounds()
    {
        var conv = new FontScaleConverter();

        conv.Convert(11d, typeof(double), "0.82;8;11", Ci).Should().Be(9.02);
        // Large base font is clamped by the max so Taskbar numbers can't overflow.
        conv.Convert(18d, typeof(double), "0.82;8;11", Ci).Should().Be(11d);
        // Small base font is lifted by the min.
        conv.Convert(8d, typeof(double), "0.64;6;9", Ci).Should().Be(6d);
    }
}
