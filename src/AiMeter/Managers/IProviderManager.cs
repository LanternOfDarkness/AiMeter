using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using AiMeter.Models;

namespace AiMeter.Managers;

public interface IProviderManager : INotifyPropertyChanged
{
    ObservableCollection<UsageMetric> Metrics { get; }

    /// <summary>
    /// Every metric name ever discovered from any provider, regardless of the user's
    /// visibility selection. Settings binds to this so deselecting a metric doesn't
    /// remove it from the list (which would make it impossible to re-enable).
    /// </summary>
    ObservableCollection<string> KnownMetricNames { get; }

    /// <summary>
    /// True once the first poll cycle has completed, regardless of whether it returned
    /// any metrics. Used to swap the widget's skeleton placeholder for real content.
    /// </summary>
    bool HasFetchedOnce { get; }

    event EventHandler<QuotaAlert>? AlertRaised;

    Task StartAsync();
    Task StopAsync();
    Task RefreshAsync();

    /// <summary>
    /// Re-applies the user's metric selection to the already-cached provider data with no
    /// network call. Used after a Settings save so the widget reflects the new selection
    /// immediately instead of waiting for the next poll. Does not raise alerts — a
    /// settings-triggered re-filter is not a new reading.
    /// </summary>
    void RefilterMetrics();
}
