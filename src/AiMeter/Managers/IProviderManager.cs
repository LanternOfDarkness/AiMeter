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

    event EventHandler<QuotaAlert>? AlertRaised;

    Task StartAsync();
    Task StopAsync();
    Task RefreshAsync();
}
