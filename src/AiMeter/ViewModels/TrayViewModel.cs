using System;
using System.Windows;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AiMeter.Managers;
using AiMeter.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AiMeter.ViewModels;

public partial class TrayViewModel : ObservableObject
{
    private readonly IProviderManager _providerManager;
    private readonly WidgetWindow _widgetWindow;
    private readonly IServiceProvider _serviceProvider;

    public TrayViewModel(IProviderManager providerManager, WidgetWindow widgetWindow, IServiceProvider serviceProvider)
    {
        _providerManager = providerManager;
        _widgetWindow = widgetWindow;
        _serviceProvider = serviceProvider;
    }

    [RelayCommand]
    private void ShowWidget()
    {
        _widgetWindow.Show();
        if (_widgetWindow.WindowState == WindowState.Minimized)
        {
            _widgetWindow.WindowState = WindowState.Normal;
        }
        _widgetWindow.Activate();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        var settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();
        settingsWindow.Show();
    }

    [RelayCommand]
    private void Exit()
    {
        Application.Current.Shutdown();
    }
}
