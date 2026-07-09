using System.Collections.ObjectModel;
using AiMeter.Managers;
using AiMeter.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiMeter.ViewModels;

public partial class WidgetViewModel : ObservableObject
{
    private readonly IProviderManager _providerManager;

    public ObservableCollection<UsageMetric> Metrics => _providerManager.Metrics;

    public WidgetViewModel(IProviderManager providerManager)
    {
        _providerManager = providerManager;
    }
}
