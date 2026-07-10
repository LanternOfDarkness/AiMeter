using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using AiMeter.Managers;
using AiMeter.Models;
using AiMeter.Services;
using AiMeter.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AiMeter.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsManager _settingsManager;
    private readonly IProviderManager _providerManager;
    private readonly IClaudeSession _claudeSession;
    private readonly IServiceProvider _serviceProvider;

    public AppConfig Config => _settingsManager.Current;

    public ObservableCollection<MetricOption> MetricOptions { get; } = new();

    public bool IsLoggedIn => Config.HasClaudeSession;
    public string LoginStatusText => IsLoggedIn ? "Status: Logged In ✔️" : "Status: Not Logged In";
    public string LoginStatusColor => IsLoggedIn ? "#2ECC71" : "#E74C3C";
    public string AccountButtonText => IsLoggedIn ? "Log out" : "Log into Claude";

    public SettingsViewModel(ISettingsManager settingsManager, IProviderManager providerManager, IClaudeSession claudeSession, IServiceProvider serviceProvider)
    {
        _settingsManager = settingsManager;
        _providerManager = providerManager;
        _claudeSession = claudeSession;
        _serviceProvider = serviceProvider;

        foreach (var name in _providerManager.KnownMetricNames)
        {
            AddMetricOption(name);
        }

        _providerManager.KnownMetricNames.CollectionChanged += KnownMetricNames_CollectionChanged;
    }

    private void KnownMetricNames_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        foreach (string name in e.NewItems)
        {
            AddMetricOption(name);
        }
    }

    private void AddMetricOption(string name)
    {
        if (MetricOptions.Any(o => o.Name == name)) return;

        var isSelected = Config.SelectedMetrics.Count == 0 || Config.SelectedMetrics.Contains(name);
        MetricOptions.Add(new MetricOption(name, isSelected));
    }

    [RelayCommand]
    private void AccountAction()
    {
        if (IsLoggedIn)
        {
            _claudeSession.Clear();
        }
        else
        {
            var authWindow = _serviceProvider.GetRequiredService<AuthWindow>();
            authWindow.ShowDialog();
        }

        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(LoginStatusText));
        OnPropertyChanged(nameof(LoginStatusColor));
        OnPropertyChanged(nameof(AccountButtonText));
    }

    [RelayCommand]
    private void Save(Window window)
    {
        Config.SelectedMetrics = MetricOptions.Where(o => o.IsSelected).Select(o => o.Name).ToList();
        _settingsManager.Save();
        window?.Close();
    }
}
