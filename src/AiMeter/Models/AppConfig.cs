using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiMeter.Models;

public partial class AppConfig : ObservableObject
{
    [ObservableProperty]
    private double _widgetOpacity = 0.9;

    [ObservableProperty]
    private int _pollingIntervalSeconds = 60;

    [ObservableProperty]
    private bool _hasClaudeSession;

    [ObservableProperty]
    private List<string> _selectedMetrics = new();

    [ObservableProperty]
    private WidgetLayoutMode _widgetLayoutMode = WidgetLayoutMode.Detailed;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private double _lowQuotaThreshold = 15;

    [ObservableProperty]
    private bool _notifyOnLowQuota = true;

    [ObservableProperty]
    private bool _notifyOnExhausted = true;

    [ObservableProperty]
    private bool _notifyOnReset = false;
}
