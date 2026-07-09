using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using AiMeter.Managers;
using AiMeter.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiMeter.ViewModels;

public partial class WidgetViewModel : ObservableObject
{
    private readonly IProviderManager _providerManager;
    private readonly ISettingsManager _settingsManager;
    private readonly DispatcherTimer _clockTimer;

    public ObservableCollection<UsageMetric> Metrics => _providerManager.Metrics;
    public AppConfig Config => _settingsManager.Current;

    [ObservableProperty]
    private DateTime _now = DateTime.Now;

    public WidgetViewModel(IProviderManager providerManager, ISettingsManager settingsManager)
    {
        _providerManager = providerManager;
        _settingsManager = settingsManager;

        // Ticks independently of provider polling so reset countdowns stay live.
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (s, e) => Now = DateTime.Now;
        _clockTimer.Start();
    }

    [RelayCommand]
    private void Hide(Window window) => window?.Hide();

    [RelayCommand]
    private void ToggleLayout()
    {
        Config.WidgetLayoutMode = Config.WidgetLayoutMode == WidgetLayoutMode.Detailed
            ? WidgetLayoutMode.Compact
            : WidgetLayoutMode.Detailed;
        _settingsManager.Save();
    }
}
