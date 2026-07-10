using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AiMeter.Converters;

/// <summary>
/// Maps a 0..1 background-opacity value to a translucent black brush, so the panel's
/// fill translucency is independent of the widget's overall (hover) opacity and the
/// text stays fully opaque.
/// </summary>
public class OpacityToBackgroundBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var opacity = value is double d ? d : 0.6;
        opacity = System.Math.Clamp(opacity, 0, 1);

        var alpha = (byte)(opacity * 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
