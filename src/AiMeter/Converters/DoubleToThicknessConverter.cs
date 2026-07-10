using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>
/// Turns a single configurable value into a uniform <see cref="Thickness"/> so the
/// widget padding can be driven by one slider. An optional ConverterParameter multiplies
/// the value (e.g. "0.6" for a tighter vertical component if ever needed).
/// </summary>
public class DoubleToThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var amount = value is double d ? d : 0;

        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var factor))
            amount *= factor;

        return new Thickness(amount);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
