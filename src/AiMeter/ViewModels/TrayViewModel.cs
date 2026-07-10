using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AiMeter.Managers;
using AiMeter.Views;

namespace AiMeter.ViewModels;

public partial class TrayViewModel : ObservableObject
{
    private readonly IProviderManager _providerManager;
    private readonly WidgetWindow _widgetWindow;
    private readonly SettingsWindow _settingsWindow;

    public TrayViewModel(IProviderManager providerManager, WidgetWindow widgetWindow, SettingsWindow settingsWindow)
    {
        _providerManager = providerManager;
        _widgetWindow = widgetWindow;
        _settingsWindow = settingsWindow;
    }

    [RelayCommand]
    private void ShowWidget()
    {
        // Re-showing the widget from the tray is intentional, so clear any prior user-hide.
        _widgetWindow.IsUserHidden = false;
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
        _settingsWindow.ShowOrActivate();
    }

    [RelayCommand]
    private void Exit()
    {
        Application.Current.Shutdown();
    }
}