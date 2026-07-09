using System.Windows;
using AiMeter.Managers;
using AiMeter.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiMeter.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsManager _settingsManager;

    public AppConfig Config => _settingsManager.Current;

    public SettingsViewModel(ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    [RelayCommand]
    private void Save(Window window)
    {
        _settingsManager.Save();
        window?.Close();
    }
}
