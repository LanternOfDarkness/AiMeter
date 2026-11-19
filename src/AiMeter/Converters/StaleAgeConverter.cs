using System.Globalization;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>
/// Formats how old cached data is from (LastUpdatedUtc, Now), so the widget's stale
/// warning is a pure function of its inputs instead of reading the system clock inline.
/// </summary>
public class StaleAgeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not DateTime lastUpdated || values[1] is not DateTime now)
            return string.Empty;

        var age = now.ToUniversalTime() - lastUpdated.ToUniversalTime();
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        if (age.TotalMinutes < 1)
            return "just now";

        if (age.TotalHours < 1)
            return $"{(int)age.TotalMinutes}m ago";

        if (age.TotalDays < 1)
            return $"{(int)age.TotalHours}h ago";

        return $"{(int)age.TotalDays}d ago";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
