using System.Globalization;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>
/// Computes the Taskbar-mode time-bar fill fraction (0-1) from (ResetTime, WindowDuration,
/// Now): elapsed / WindowDuration, where elapsed = now - (ResetTime - WindowDuration). Fills
/// as the reset approaches (0 = just reset, 1 = about to reset). Returns 0 when either the
/// reset time or the window duration is unknown, which collapses the bar to zero height.
/// </summary>
public class TimeBarFractionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3) return 0.0;
        if (values[0] is not DateTime resetTime) return 0.0;
        if (values[1] is not TimeSpan windowDuration || windowDuration <= TimeSpan.Zero) return 0.0;
        if (values[2] is not DateTime now) return 0.0;

        var windowStart = resetTime - windowDuration;
        var elapsed = now - windowStart;
        var fraction = elapsed.TotalSeconds / windowDuration.TotalSeconds;
        return Math.Clamp(fraction, 0.0, 1.0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
