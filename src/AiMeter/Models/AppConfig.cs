using System.Collections.Generic;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiMeter.Models;

public partial class AppConfig : ObservableObject
{
    [ObservableProperty]
    private double _widgetOpacity = 0.9;

    [ObservableProperty]
    private bool _opacityEnabled = true;

    [ObservableProperty]
    private double _hoverOpacity = 0.35;

    [ObservableProperty]
    private double _widgetPadding = 6;

    [ObservableProperty]
    private double _backgroundOpacity = 0.6;

    [ObservableProperty]
    private double? _widgetLeft;

    [ObservableProperty]
    private double? _widgetTop;

    [ObservableProperty]
    private int _pollingIntervalSeconds = 60;

    [ObservableProperty]
    private bool _hasClaudeSession;

    [ObservableProperty]
    private bool _hasOpenCodeSession;

    [ObservableProperty]
    private List<string> _selectedMetrics = new();

    // Serialized under a new JSON name ("LayoutMode", not "WidgetLayoutMode") deliberately:
    // the old enum stored Detailed=0/Compact=1 as a bare number, and the new enum reuses
    // value 1 for Taskbar. Keeping the old property name would silently reinterpret a
    // pre-upgrade user's saved "Compact" (1) as "Taskbar" on load. The stale field is now
    // just an unknown property that System.Text.Json ignores.
    [ObservableProperty]
    [property: JsonPropertyName("LayoutMode")]
    private WidgetLayoutMode _widgetLayoutMode = WidgetLayoutMode.Compact;

    [ObservableProperty]
    private bool _showOverFullscreen = true;

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
