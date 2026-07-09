using System;
using System.Windows;
using AiMeter.Managers;
using AiMeter.Models;
using AiMeter.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AiMeter.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsManager _settingsManager;
    private readonly IServiceProvider _serviceProvider;

    public AppConfig Config => _settingsManager.Current;

    public SettingsViewModel(ISettingsManager settingsManager, IServiceProvider serviceProvider)
    {
        _settingsManager = settingsManager;
        _serviceProvider = serviceProvider;
    }

    [RelayCommand]
    private void LoginToClaude()
    {
        var authWindow = _serviceProvider.GetRequiredService<AuthWindow>();
        authWindow.ShowDialog();
    }

    [RelayCommand]
    private void Save(Window window)
    {
        _settingsManager.Save();
        window?.Close();
    }
}
