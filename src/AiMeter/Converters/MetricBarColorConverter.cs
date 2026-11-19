using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AiMeter.Models;

namespace AiMeter.Converters;

/// <summary>
/// Resolves a metric bar's brush from its live scalars - remaining percentage, mode, clock,
/// reset time, window length and custom color. Deliberately takes every input as its own
/// binding rather than the metric object: WPF only re-runs a MultiBinding when one of its child
/// bindings changes, so a single "path=." source would leave the bar's color stale after a
/// poll updates the metric (e.g. red at 100% right after a reset) until the next clock tick.
/// A per-metric custom color always wins; otherwise the mode picks between the fixed quota
/// thresholds, a smooth remaining-percentage gradient, and a quota-vs-time-to-reset
/// ("pace") color.
/// </summary>
public class MetricBarColorConverter : IMultiValueConverter
{
    // Progressive palette: empty -> full, interpolated in sRGB between adjacent stops.
    private static readonly (double Stop, Color Color)[] ProgressiveStops =
    {
        (0, Color.FromRgb(0x8B, 0x00, 0x00)),
        (10, Color.FromRgb(0xE7, 0x4C, 0x3C)),
        (25, Color.FromRgb(0xE6, 0x7E, 0x22)),
        (50, Color.FromRgb(0xF1, 0xC4, 0x0F)),
        (75, Color.FromRgb(0x34, 0x98, 0xDB)),
        (100, Color.FromRgb(0x2E, 0xCC, 0x71)),
    };

    // Pace anchors: -10% behind budget (green), exactly on pace (blue), +10% over (red).
    private const double PaceAnchor = 0.10;
    private static readonly Color UnderPaceColor = Color.FromRgb(0x2E, 0xCC, 0x71);
    private static readonly Color OnPaceColor = Color.FromRgb(0x34, 0x98, 0xDB);
    private static readonly Color OverPaceColor = Color.FromRgb(0xE7, 0x4C, 0x3C);

    private readonly QuotaToColorConverter _quota = new();

    /// <param name="values">
    /// 0: remaining percentage; 1: <see cref="BarColorMode"/>; 2: current time; 3: reset time;
    /// 4: window duration; 5: custom color hex.
    /// </param>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var remaining = values.Length > 0 && values[0] is double pct ? pct : 0d;
        var mode = values.Length > 1 && values[1] is BarColorMode m ? m : BarColorMode.Threshold;
        var now = values.Length > 2 && values[2] is DateTime t ? t : DateTime.Now;
        var reset = values.Length > 3 ? values[3] as DateTime? : null;
        var duration = values.Length > 4 ? values[4] as TimeSpan? : null;
        var customColor = values.Length > 5 ? values[5] as string : null;

        if (!string.IsNullOrWhiteSpace(customColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(customColor);
                return FrozenBrush(color);
            }
            catch
            {
                // Malformed hex in settings shouldn't blank the bar - fall through to the mode color.
            }
        }

        // A zero/negative window can't define a pace - fall back to the thresholds below.
        if (mode == BarColorMode.Pace
            && duration is { } window && window > TimeSpan.Zero
            && reset is { } resetsAt)
        {
            return FrozenBrush(PaceColor(remaining, resetsAt, window, now));
        }

        if (mode == BarColorMode.Progressive)
            return FrozenBrush(ProgressiveColor(remaining));

        return _quota.Convert(remaining, targetType, parameter, culture);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();

    private static Color ProgressiveColor(double remainingPercentage)
    {
        var pct = Math.Clamp(remainingPercentage, 0, 100);

        for (var i = 1; i < ProgressiveStops.Length; i++)
        {
            var (toStop, toColor) = ProgressiveStops[i];
            if (pct > toStop) continue;

            var (fromStop, fromColor) = ProgressiveStops[i - 1];
            var fraction = (pct - fromStop) / (toStop - fromStop);
            return Lerp(fromColor, toColor, fraction);
        }

        return ProgressiveStops[^1].Color;
    }

    private static Color PaceColor(double remainingPercentage, DateTime reset, TimeSpan duration, DateTime now)
    {
        var windowStart = reset - duration;
        var timeElapsed = Math.Clamp((now - windowStart).TotalSeconds / duration.TotalSeconds, 0, 1);
        var quotaUsed = 1 - remainingPercentage / 100;
        var delta = Math.Clamp(quotaUsed - timeElapsed, -PaceAnchor, PaceAnchor);

        return delta <= 0
            ? Lerp(UnderPaceColor, OnPaceColor, (delta + PaceAnchor) / PaceAnchor)
            : Lerp(OnPaceColor, OverPaceColor, delta / PaceAnchor);
    }

    private static Color Lerp(Color from, Color to, double fraction)
        => Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * fraction),
            (byte)Math.Round(from.G + (to.G - from.G) * fraction),
            (byte)Math.Round(from.B + (to.B - from.B) * fraction));

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
