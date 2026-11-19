using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using AiMeter.Interop;
using AiMeter.ViewModels;

namespace AiMeter.Views;

public partial class UpdateWindow : Window
{
    public UpdateWindow(UpdateViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Applied here (not the ctor) so the HWND is only created when the window is
        // actually shown - avoids leaving a hidden, ghost HWND behind for this DI singleton.
        SourceInitialized += (s, e) => DarkTitleBar.ApplyToHwnd(new WindowInteropHelper(this).Handle);

        Closing += OnClosing;
    }

    /// <summary>Shows the singleton window, or activates it if already open.</summary>
    public void ShowOrActivate()
    {
        if (DataContext is UpdateViewModel vm)
        {
            vm.EnsureFirstCheck();
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
        // A running download must be stopped explicitly with Cancel; hiding here would
        // orphan the progress (and the staging file) with no visible way to stop it.
        if (DataContext is UpdateViewModel { IsDownloading: true })
        {
            e.Cancel = true;
            return;
        }

        // A singleton window must never actually Close() (that disposes it for the rest of
        // the app lifetime), so intercept the X-button and convert it into a hide.
        e.Cancel = true;
        Hide();
    }
}
