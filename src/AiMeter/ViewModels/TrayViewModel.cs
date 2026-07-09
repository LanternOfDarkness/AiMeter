using System.Windows;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AiMeter.Managers;
using AiMeter.Views;

namespace AiMeter.ViewModels;

public partial class TrayViewModel : ObservableObject
{
    private readonly IProviderManager _providerManager;
    private readonly WidgetWindow _widgetWindow;

    public TrayViewModel(IProviderManager providerManager, WidgetWindow widgetWindow)
    {
        _providerManager = providerManager;
        _widgetWindow = widgetWindow;
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
        // TODO: Show settings window
        MessageBox.Show("Show Settings command invoked", "AiMeter");
    }

    [RelayCommand]
    private void Exit()
    {
        Application.Current.Shutdown();
    }
}
