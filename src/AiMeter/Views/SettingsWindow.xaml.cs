using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using AiMeter.Interop;
using AiMeter.ViewModels;

namespace AiMeter.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Applied here (not the ctor) so the HWND is only created when the window is
        // actually shown - avoids leaving a hidden, ghost HWND behind for this DI singleton.
        SourceInitialized += (s, e) => DarkTitleBar.ApplyToHwnd(new WindowInteropHelper(this).Handle);

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