using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>Visible when the bound boolean is false; collapsed when true.</summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}