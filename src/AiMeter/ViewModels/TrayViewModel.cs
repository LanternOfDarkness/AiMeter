using System.Windows;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AiMeter.Managers;

namespace AiMeter.ViewModels;

public partial class TrayViewModel : ObservableObject
{
    private readonly IProviderManager _providerManager;

    public TrayViewModel(IProviderManager providerManager)
    {
        _providerManager = providerManager;
    }

    [RelayCommand]
    private void ShowWidget()
    {
        var metricsCount = _providerManager.Metrics.Count;
        var names = string.Join(", ", _providerManager.Metrics.Select(m => m.Name));
        MessageBox.Show($"Widget would show {metricsCount} metrics:\n{names}", "AiMeter");
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
