using CommunityToolkit.Mvvm.ComponentModel;

namespace AiMeter.Models;

public partial class AppConfig : ObservableObject
{
    [ObservableProperty]
    private double _widgetOpacity = 0.9;

    [ObservableProperty]
    private int _pollingIntervalSeconds = 60;

    [ObservableProperty]
    private string? _encryptedCookies;

    [ObservableProperty]
    private System.Collections.Generic.List<string> _selectedMetrics = new();
}
