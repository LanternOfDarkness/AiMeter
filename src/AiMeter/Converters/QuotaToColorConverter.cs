using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AiMeter.Converters;

public class QuotaToColorConverter : IValueConverter
{
    public Brush HighQuotaBrush { get; set; } = new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Green
    public Brush MediumQuotaBrush { get; set; } = new SolidColorBrush(Color.FromRgb(241, 196, 15)); // Yellow/Orange
    public Brush LowQuotaBrush { get; set; } = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Red

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double remainingPercentage)
        {
            if (remainingPercentage <= 20) return LowQuotaBrush;
            if (remainingPercentage <= 50) return MediumQuotaBrush;
            return HighQuotaBrush;
        }

        return HighQuotaBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
