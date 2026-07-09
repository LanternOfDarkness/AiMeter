using System.Collections.ObjectModel;
using AiMeter.Managers;
using AiMeter.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiMeter.ViewModels;

public partial class WidgetViewModel : ObservableObject
{
    private readonly IProviderManager _providerManager;
    private readonly ISettingsManager _settingsManager;

    public ObservableCollection<UsageMetric> Metrics => _providerManager.Metrics;
    public AppConfig Config => _settingsManager.Current;

    public WidgetViewModel(IProviderManager providerManager, ISettingsManager settingsManager)
    {
        _providerManager = providerManager;
        _settingsManager = settingsManager;
    }
}
