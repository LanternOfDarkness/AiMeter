using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using AiMeter.Models;

namespace AiMeter.Managers;

public interface IProviderManager : INotifyPropertyChanged
{
    ObservableCollection<UsageMetric> Metrics { get; }

    event EventHandler<QuotaAlert>? AlertRaised;

    Task StartAsync();
    Task StopAsync();
    Task RefreshAsync();
}
