using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AiMeter.Converters;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class CustomizationConverterTests
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

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

    [Fact]
    public void MetricColor_uses_custom_hex_when_present()
    {
        var conv = new MetricColorConverter();
        var brush = (SolidColorBrush)conv.Convert(new object[] { 80d, "#112233" }, typeof(Brush), null!, Ci);
        brush.Color.Should().Be((Color)ColorConverter.ConvertFromString("#112233"));
    }

    [Fact]
    public void MetricColor_falls_back_to_quota_palette_when_no_custom_color()
    {
        var conv = new MetricColorConverter();

        var high = (SolidColorBrush)conv.Convert(new object[] { 80d, null }, typeof(Brush), null!, Ci);
        high.Color.Should().Be(Color.FromRgb(46, 204, 113), "high quota is green");

        var low = (SolidColorBrush)conv.Convert(new object[] { 10d, "" }, typeof(Brush), null!, Ci);
        low.Color.Should().Be(Color.FromRgb(231, 76, 60), "an empty override still means low-quota red");
    }

    [Fact]
    public void MetricColor_ignores_malformed_hex_and_uses_quota_palette()
    {
        var conv = new MetricColorConverter();
        var brush = (SolidColorBrush)conv.Convert(new object[] { 80d, "not-a-color" }, typeof(Brush), null!, Ci);
        brush.Color.Should().Be(Color.FromRgb(46, 204, 113));
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
