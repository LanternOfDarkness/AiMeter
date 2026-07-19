using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AiMeter.Converters;

/// <summary>
/// Resolves a metric bar's brush from [remainingPercentage, customColorHex]. When the user has
/// set a per-metric color it wins; otherwise this falls back to the same quota-threshold palette
/// as <see cref="QuotaToColorConverter"/> so uncustomized metrics look exactly as before.
/// </summary>
public class MetricColorConverter : IMultiValueConverter
{
    private readonly QuotaToColorConverter _quota = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var percentage = values.Length > 0 && values[0] is double p ? p : 0d;
        var customColor = values.Length > 1 ? values[1] as string : null;

        if (!string.IsNullOrWhiteSpace(customColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(customColor);
                return new SolidColorBrush(color);
            }
            catch
            {
                // Malformed hex in settings shouldn't blank the bar - fall through to quota color.
            }
        }

        return _quota.Convert(percentage, targetType, parameter, culture);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
