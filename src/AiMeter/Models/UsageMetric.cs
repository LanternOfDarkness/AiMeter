using CommunityToolkit.Mvvm.ComponentModel;

namespace AiMeter.Models;

/// <summary>
/// Notifies on change so the widget's ItemsControl-bound tiles (bars, labels)
/// refresh their values via data binding instead of the container being torn down and
/// recreated on every poll (see ProviderManager.SyncMetrics, which updates instances in
/// place rather than replacing collection entries).
/// </summary>
public partial class UsageMetric : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// The label the widget actually renders in Compact mode. Defaults to <see cref="Name"/>,
    /// but <see cref="Managers.ProviderManager"/> overwrites it from the user's per-metric label
    /// overrides (AppConfig.MetricLabels). Not provider data, so it's set outside
    /// <see cref="UpdateFrom"/> and defaults back to Name when no override exists.
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// Optional per-metric bar color (hex) chosen by the user. Null = use the quota-threshold
    /// palette. Set from AppConfig.MetricColors, not from the provider (see DisplayName note).
    /// </summary>
    [ObservableProperty]
    private string? _customColor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingPercentage))]
    private double _remainingQuota;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingPercentage))]
    private double _totalQuota;

    [ObservableProperty]
    private DateTime? _resetTime;

    [ObservableProperty]
    private string? _providerIconUrl;

    /// <summary>
    /// Length of this metric's reset window (e.g. 5h for a session limit, 7d for a weekly
    /// one), used only to compute the Taskbar-mode time-bar fraction. Null for placeholder
    /// tiles (auth-required, error, unknown kinds) - the time bar hides in that case.
    /// </summary>
    [ObservableProperty]
    private TimeSpan? _windowDuration;

    public double RemainingPercentage => TotalQuota > 0 ? (RemainingQuota / TotalQuota) * 100 : 0;

    public void UpdateFrom(UsageMetric other)
    {
        Name = other.Name;
        RemainingQuota = other.RemainingQuota;
        TotalQuota = other.TotalQuota;
        ResetTime = other.ResetTime;
        ProviderIconUrl = other.ProviderIconUrl;
        WindowDuration = other.WindowDuration;
    }
}
