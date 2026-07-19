using CommunityToolkit.Mvvm.ComponentModel;

namespace AiMeter.Models;

public partial class MetricOption : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Optional per-metric display name shown in Compact mode; empty = use Name.</summary>
    [ObservableProperty]
    private string _customLabel = string.Empty;

    /// <summary>Optional per-metric bar color (hex); null = quota-threshold palette.</summary>
    [ObservableProperty]
    private string? _customColor;

    public MetricOption(string name, bool isSelected, string customLabel = "", string? customColor = null)
    {
        Name = name;
        _isSelected = isSelected;
        _customLabel = customLabel;
        _customColor = customColor;
    }
}
