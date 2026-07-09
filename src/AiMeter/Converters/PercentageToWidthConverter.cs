using System.Globalization;
using System.Windows.Data;

namespace AiMeter.Converters;

public class PercentageToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double percentage) return 0d;

        var maxWidth = 120d;
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            maxWidth = parsed;
        }

        var clamped = Math.Clamp(percentage, 0, 100);
        return maxWidth * clamped / 100.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
