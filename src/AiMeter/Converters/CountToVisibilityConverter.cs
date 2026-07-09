using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>Visible when a collection's Count equals zero (used for "empty list" hints).</summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
