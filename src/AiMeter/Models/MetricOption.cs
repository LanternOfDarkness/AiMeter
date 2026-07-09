using CommunityToolkit.Mvvm.ComponentModel;

namespace AiMeter.Models;

public partial class MetricOption : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    public MetricOption(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }
}
