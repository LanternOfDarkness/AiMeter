using System.Windows;
using AiMeter.Interop;
using AiMeter.ViewModels;

namespace AiMeter.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        DarkTitleBar.Apply(this);
    }
}
