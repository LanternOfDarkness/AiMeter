using System.Globalization;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>
/// Formats a reset countdown from (ResetTime, Now) instead of reading the system
/// clock inline in the model, so the text is a pure, testable function of its inputs.
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

        var timeSpan = resetTime.Value - now;
        if (timeSpan.TotalMinutes < 0) return "Resetting...";
        if (timeSpan.TotalHours >= 1)
            return $"Resets in {(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
        return $"Resets in {timeSpan.Minutes}m";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
