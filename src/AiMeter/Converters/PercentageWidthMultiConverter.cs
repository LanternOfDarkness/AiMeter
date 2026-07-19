using System.Globalization;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>
/// Bindable counterpart to <see cref="PercentageToWidthConverter"/>: turns
/// [remainingPercentage, maxWidth] into a clamped pixel width. Used by the Compact bar fill so
/// the track width can be driven by a data-bound setting (AppConfig.BarWidth) instead of a
/// hardcoded ConverterParameter.
/// </summary>
public class PercentageWidthMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double percentage || values[1] is not double maxWidth)
            return 0d;

        var clamped = Math.Clamp(percentage, 0, 100);
        return maxWidth * clamped / 100.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
