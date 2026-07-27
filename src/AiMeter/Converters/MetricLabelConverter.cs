using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace AiMeter.Converters;

/// <summary>
/// Maps a metric's display name to a 3-letter code for Taskbar mode's narrow columns.
/// Several names are dynamic (scoped weeklies embed a model name, placeholder tiles carry
/// full sentences), so this matches by prefix/pattern rather than exact lookup - longest,
/// most-specific prefix first.
/// </summary>
public class MetricLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value as string ?? string.Empty;
        return CodeFor(name);
    }

    public static string CodeFor(string name)
    {
        if (name.Contains("Required", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Expired", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Error", StringComparison.OrdinalIgnoreCase))
        {
            return "!";
        }

        if (name.StartsWith("Claude Session", StringComparison.Ordinal)) return "CSE";
        if (name.StartsWith("Claude Weekly (", StringComparison.Ordinal)) return "CWS";
        if (name.StartsWith("Claude Weekly", StringComparison.Ordinal)) return "CWK";
        if (name.StartsWith("Claude Extra", StringComparison.Ordinal)) return "CEU";
        if (name.StartsWith("OpenCode Rolling", StringComparison.Ordinal)) return "OCR";
        if (name.StartsWith("OpenCode Weekly", StringComparison.Ordinal)) return "OCW";
        if (name.StartsWith("OpenCode Monthly", StringComparison.Ordinal)) return "OCM";

        var letters = new string(name.Where(char.IsLetter).ToArray());
        return (letters.Length >= 3 ? letters[..3] : letters.PadRight(3, '?')).ToUpperInvariant();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
