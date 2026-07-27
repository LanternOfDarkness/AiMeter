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

    /// <summary>
    /// Marks this as a pay-as-you-go money metric (Claude Extra Usage) rather than a plain
    /// quota. When true, the widget shows dollar text (<see cref="ValueText"/>) instead of a
    /// bare percentage, sourced from <see cref="UsedAmount"/>/<see cref="LimitAmount"/>/
    /// <see cref="BalanceAmount"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    [NotifyPropertyChangedFor(nameof(ValueTextShort))]
    [NotifyPropertyChangedFor(nameof(TooltipDetail))]
    private bool _isMoneyMetric;

    /// <summary>
    /// True when a money metric has no monthly cap to measure against (Claude's "Unlimited"
    /// Extra Usage plan) - <see cref="RemainingPercentage"/> is meaningless here, so the
    /// widget hides the bar and shows spend/balance text only.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    [NotifyPropertyChangedFor(nameof(ValueTextShort))]
    [NotifyPropertyChangedFor(nameof(TooltipDetail))]
    private bool _isUnbounded;

    /// <summary>Amount spent so far this period, in major currency units (e.g. dollars).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    [NotifyPropertyChangedFor(nameof(ValueTextShort))]
    [NotifyPropertyChangedFor(nameof(TooltipDetail))]
    private double? _usedAmount;

    /// <summary>Monthly spend cap in major currency units. Null/absent when <see cref="IsUnbounded"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    [NotifyPropertyChangedFor(nameof(ValueTextShort))]
    [NotifyPropertyChangedFor(nameof(TooltipDetail))]
    private double? _limitAmount;

    /// <summary>
    /// Prepaid balance left, in major currency units - only meaningful for unbounded plans
    /// that draw down a purchased credit balance instead of stopping at a hard cap.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    [NotifyPropertyChangedFor(nameof(ValueTextShort))]
    [NotifyPropertyChangedFor(nameof(TooltipDetail))]
    private double? _balanceAmount;

    /// <summary>ISO currency code (e.g. "USD") used to pick the symbol for <see cref="ValueText"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    [NotifyPropertyChangedFor(nameof(ValueTextShort))]
    [NotifyPropertyChangedFor(nameof(TooltipDetail))]
    private string? _currency;

    public double RemainingPercentage => TotalQuota > 0 ? (RemainingQuota / TotalQuota) * 100 : 0;

    private string CurrencySymbol => Currency is "USD" or null ? "$" : Currency + " ";

    /// <summary>
    /// Compact-mode value column text. Non-money metrics keep showing a bare percentage there
    /// (via direct binding to <see cref="RemainingPercentage"/>) - this is only consulted when
    /// <see cref="IsMoneyMetric"/> is true.
    /// </summary>
    public string ValueText
    {
        get
        {
            if (!IsMoneyMetric) return string.Empty;
            var sym = CurrencySymbol;
            if (!IsUnbounded && LimitAmount is { } limit)
                return $"{sym}{UsedAmount ?? 0:0.00} / {sym}{limit:0.00}";
            if (BalanceAmount is { } bal)
                return $"{sym}{bal:0.00} left";
            return $"{sym}{UsedAmount ?? 0:0.00} used";
        }
    }

    /// <summary>Taskbar mode's ~28px column can't fit full dollar text - a rounded, unit-less number.</summary>
    public string ValueTextShort
    {
        get
        {
            if (!IsMoneyMetric) return string.Empty;
            if (!IsUnbounded) return RemainingPercentage.ToString("0");
            var amount = BalanceAmount ?? UsedAmount ?? 0;
            return CurrencySymbol + amount.ToString("0");
        }
    }

    /// <summary>Extra tooltip line spelling out the spend/limit/balance behind <see cref="ValueText"/>.</summary>
    public string TooltipDetail
    {
        get
        {
            if (!IsMoneyMetric) return string.Empty;
            var sym = CurrencySymbol;
            if (!IsUnbounded && LimitAmount is { } limit)
                return $"{sym}{UsedAmount ?? 0:0.00} of {sym}{limit:0.00} used";
            if (BalanceAmount is { } bal)
                return $"{sym}{bal:0.00} balance left ({sym}{UsedAmount ?? 0:0.00} used)";
            return $"{sym}{UsedAmount ?? 0:0.00} used (no spend cap)";
        }
    }

    public void UpdateFrom(UsageMetric other)
    {
        Name = other.Name;
        RemainingQuota = other.RemainingQuota;
        TotalQuota = other.TotalQuota;
        ResetTime = other.ResetTime;
        ProviderIconUrl = other.ProviderIconUrl;
        WindowDuration = other.WindowDuration;
        IsMoneyMetric = other.IsMoneyMetric;
        IsUnbounded = other.IsUnbounded;
        UsedAmount = other.UsedAmount;
        LimitAmount = other.LimitAmount;
        BalanceAmount = other.BalanceAmount;
        Currency = other.Currency;
    }
}
