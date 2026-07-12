using System.Collections.Generic;
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

    /// <summary>
    /// The user's OpenAI Platform API key, DPAPI-encrypted (CurrentUser) and base64-encoded — the
    /// OpenAI provider authenticates with a key rather than a browser session, so unlike the
    /// Claude/OpenCode session markers this holds a real secret and must never be stored in clear.
    /// Managed via <see cref="AiMeter.Services.OpenAiSession"/>; null/empty means "not configured."
    /// </summary>
    [ObservableProperty]
    private string? _openAiApiKeyProtected;

    [ObservableProperty]
    private List<string> _selectedMetrics = new();

    [ObservableProperty]
    private WidgetLayoutMode _widgetLayoutMode = WidgetLayoutMode.Detailed;

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
