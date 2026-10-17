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
    private readonly WidgetHostManager _widgetHost;
    private readonly SettingsWindow _settingsWindow;

    public TrayViewModel(IProviderManager providerManager, WidgetHostManager widgetHost, SettingsWindow settingsWindow)
    {
        _providerManager = providerManager;
        _widgetHost = widgetHost;
        _settingsWindow = settingsWindow;
    }

    [RelayCommand]
    private void ShowWidget()
    {
        // Delegated to the host manager: in Deskband mode there is no floating window to
        // re-show (the taskbar/library owns visibility), so this is intentionally a no-op.
        _widgetHost.ShowWidget();
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