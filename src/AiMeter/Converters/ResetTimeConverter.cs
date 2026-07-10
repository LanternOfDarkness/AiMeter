using System.Globalization;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>
/// Formats a reset countdown from (ResetTime, Now) instead of reading the system
/// clock inline in the model, so the text is a pure, testable function of its inputs.
///
/// Pass ConverterParameter="short" for the compact widget rows (e.g. "5h", "2d 3h",
/// "12m") with no "Resets in " prefix; the default verbose form is used otherwise.
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

        var isShort = parameter is string s && s.Equals("short", StringComparison.OrdinalIgnoreCase);

        var timeSpan = resetTime.Value - now;
        if (timeSpan.TotalMinutes < 0)
            return isShort ? "now" : "Resetting...";

        var text = FormatDuration(timeSpan);
        return isShort ? text : $"Resets in {text}";
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

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
