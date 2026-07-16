using System.Globalization;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>
/// Formats a reset countdown from (ResetTime, Now) instead of reading the system
/// clock inline in the model, so the text is a pure, testable function of its inputs.
///
/// Pass ConverterParameter="short" for the compact widget rows (e.g. "5h", "2d 3h",
/// "12m") with no "Resets in " prefix; ConverterParameter="decimal" for the single
/// number+unit form Taskbar mode's narrow columns need (e.g. "3.3h", "5.2d", "12m");
/// the default verbose form is used otherwise.
/// </summary>
public class ResetTimeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return string.Empty;

        var resetTime = values[0] as DateTime?;
        if (resetTime is null || values[1] is not DateTime now)
            return string.Empty;

        var mode = parameter as string;
        var isShort = mode is not null && mode.Equals("short", StringComparison.OrdinalIgnoreCase);
        var isDecimal = mode is not null && mode.Equals("decimal", StringComparison.OrdinalIgnoreCase);

        var timeSpan = resetTime.Value - now;
        if (timeSpan.TotalMinutes < 0)
            return isShort || isDecimal ? "now" : "Resetting...";

        var text = isDecimal ? FormatDurationDecimal(timeSpan) : FormatDuration(timeSpan);
        return isShort || isDecimal ? text : $"Resets in {text}";
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 24)
        {
            var days = (int)span.TotalDays;
            return $"{days}d {span.Hours}h";
        }

        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes}m";

        return $"{span.Minutes}m";
    }

    /// <summary>
    /// Single number+unit form for Taskbar mode's ~24px-wide columns - "3h20m" both reads
    /// slower and is wider than a one-decimal value like "3.3h" at this scale.
    /// </summary>
    private static string FormatDurationDecimal(TimeSpan span)
    {
        if (span.TotalHours >= 24)
            return $"{span.TotalDays.ToString("0.0", CultureInfo.InvariantCulture)}d";

        if (span.TotalHours >= 1)
            return $"{span.TotalHours.ToString("0.0", CultureInfo.InvariantCulture)}h";

        return $"{(int)span.TotalMinutes}m";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
