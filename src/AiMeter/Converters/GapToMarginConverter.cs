using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>
/// Turns a single gap value into a one-sided <see cref="Thickness"/>, so spacing settings can
/// drive margins without a dedicated converter per edge. ConverterParameter picks the edge:
/// <list type="bullet">
/// <item>"Left" (default) — (gap,0,0,0); spaces the cells of a Compact row apart.</item>
/// <item>"Right" — (0,0,gap,0); trails each Compact row so wrapped columns separate.</item>
/// <item>"NegativeRight" — (0,0,-gap,0); applied to the wrapping panel it cancels the trailing
/// gap left by the last column, keeping the widget's inner padding symmetric.</item>
/// </list>
/// Vertical components stay zero on purpose: the Compact column-wrap math depends on a row's
/// vertical footprint being exactly its fixed Height.
/// </summary>
public class GapToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var gap = value is double d ? d : 0;

        return (parameter as string) switch
        {
            "Right" => new Thickness(0, 0, gap, 0),
            "NegativeRight" => new Thickness(0, 0, -gap, 0),
            _ => new Thickness(gap, 0, 0, 0),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
