using System.ComponentModel;
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

        // A singleton window must never actually Close() (that disposes it for the rest of
        // the app lifetime), so intercept the X-button and convert it into a hide.
        Closing += OnClosing;
    }

    /// <summary>
    /// Shows the singleton settings window, or activates it if already open. Also resyncs
    /// the VM's checkbox state from the saved config so reopened unsaved edits never linger.
    /// </summary>
    public void ShowOrActivate()
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.ResyncMetricSelections();
        }

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}