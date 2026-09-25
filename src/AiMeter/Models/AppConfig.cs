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

    // --- Compact-mode layout customization (defaults match the previously hardcoded values,
    // so existing users see no visual change on upgrade). ---

    /// <summary>
    /// Compact mode: how many bars stack in one column before wrapping into the next column.
    /// 0 means a single unbounded column (the pre-1.2 behavior).
    /// </summary>
    [ObservableProperty]
    private int _barsPerColumn = 6;

    /// <summary>Compact mode: pixel width of each quota bar's track.</summary>
    [ObservableProperty]
    private double _barWidth = 120;

    /// <summary>Compact mode: horizontal gap between the cells of a row (label / bar / % / reset).</summary>
    [ObservableProperty]
    private double _compactCellGap = 8;

    /// <summary>
    /// Compact mode: horizontal gap between wrapped bar columns. Only visible once
    /// <see cref="BarsPerColumn"/> forces a second column.
    /// </summary>
    [ObservableProperty]
    private double _compactColumnGap = 14;

    /// <summary>Compact mode: base font size for the metric label (secondary text derives from it).</summary>
    [ObservableProperty]
    private double _widgetFontSize = 11;

    /// <summary>
    /// When false, the always-visible % and reset text are hidden in both layouts, leaving
    /// just the label/code and the colored bar; the numbers stay available on hover.
    /// </summary>
    [ObservableProperty]
    private bool _showNumericValues = true;

    /// <summary>Per-metric display-name overrides (metric Name → custom label). Compact mode only.</summary>
    [ObservableProperty]
    private Dictionary<string, string> _metricLabels = new();

    /// <summary>Per-metric bar-color overrides (metric Name → hex like "#2ECC71"); absent = quota palette.</summary>
    [ObservableProperty]
    private Dictionary<string, string> _metricColors = new();

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

    /// <summary>
    /// Every real metric name ever reported, so a metric seen for the first time (e.g. right
    /// after logging into a new provider) is auto-selected once, while one the user has since
    /// unchecked stays unchecked. Null = settings file predates this field.
    /// </summary>
    [ObservableProperty]
    private List<string>? _seenMetrics;

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
