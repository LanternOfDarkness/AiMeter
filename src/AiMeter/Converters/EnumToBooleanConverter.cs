using System.Globalization;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>Binds a RadioButton's IsChecked to one value of an enum-typed property.</summary>
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not bool isChecked || !isChecked || parameter is null)
            return Binding.DoNothing;

        return Enum.Parse(targetType, parameter.ToString()!);
    }
}
