using CommunityToolkit.Mvvm.ComponentModel;

namespace AiMeter.Models;

/// <summary>
/// Notifies on change so the widget's ItemsControl-bound tiles (CircularProgress, labels)
/// refresh their values via data binding instead of the container being torn down and
/// recreated on every poll (see ProviderManager.SyncMetrics, which updates instances in
/// place rather than replacing collection entries).
/// </summary>
public partial class UsageMetric : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

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

    public double RemainingPercentage => TotalQuota > 0 ? (RemainingQuota / TotalQuota) * 100 : 0;

    public void UpdateFrom(UsageMetric other)
    {
        Name = other.Name;
        RemainingQuota = other.RemainingQuota;
        TotalQuota = other.TotalQuota;
        ResetTime = other.ResetTime;
        ProviderIconUrl = other.ProviderIconUrl;
    }
}
