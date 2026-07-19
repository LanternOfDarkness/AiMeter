using System.Globalization;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>
/// Scales a base font size (AppConfig.WidgetFontSize) by a factor with optional clamping, so
/// several text elements can derive from one setting. ConverterParameter format is
/// "factor" or "factor;min;max" (e.g. "0.9" for Compact secondary text, "0.8;6;10" for Taskbar
/// numbers that must stay within the docked column height).
/// </summary>
public class FontScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var size = value is double d ? d : 11d;
        var factor = 1d;
        double? min = null, max = null;

        if (parameter is string s)
        {
            var parts = s.Split(';');
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var f))
                factor = f;
            if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var mn))
                min = mn;
            if (parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var mx))
                max = mx;
        }

        var scaled = size * factor;
        if (min is not null) scaled = Math.Max(scaled, min.Value);
        if (max is not null) scaled = Math.Min(scaled, max.Value);
        return scaled;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
