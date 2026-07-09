using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiMeter.ViewModels;

public partial class TrayViewModel : ObservableObject
{
    public TrayViewModel()
    {
    }

    [RelayCommand]
    private void ShowWidget()
    {
        // TODO: Show the widget window
        MessageBox.Show("Show Widget command invoked", "AiMeter");
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
