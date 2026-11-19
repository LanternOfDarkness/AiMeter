using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AiMeter.Managers;
using AiMeter.Services.Updates;
using AiMeter.Views;

namespace AiMeter.ViewModels;

public partial class TrayViewModel : ObservableObject
{
    private readonly IProviderManager _providerManager;
    private readonly WidgetHostManager _widgetHost;
    private readonly SettingsWindow _settingsWindow;
    private readonly IUpdateManager _updateManager;
    private readonly UpdateWindow _updateWindow;
    private readonly IUiDispatcher _dispatcher;

    public TrayViewModel(
        IProviderManager providerManager,
        WidgetHostManager widgetHost,
        SettingsWindow settingsWindow,
        IUpdateManager updateManager,
        UpdateWindow updateWindow,
        IUiDispatcher dispatcher)
    {
        _providerManager = providerManager;
        _widgetHost = widgetHost;
        _settingsWindow = settingsWindow;
        _updateManager = updateManager;
        _updateWindow = updateWindow;
        _dispatcher = dispatcher;

        _updateManager.StateChanged += OnUpdateStateChanged;
    }

    /// <summary>Drives the visibility of the "Update to vX.Y.Z…" tray item.</summary>
    public bool HasAvailableUpdate => _updateManager.HasAvailableUpdate;

    /// <summary>Header of the conditional tray item; empty when no update is on offer.</summary>
    public string AvailableUpdateText =>
        _updateManager.AvailableRelease is { } release ? $"Update to v{release.Version}\u2026" : string.Empty;

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
    private void ShowUpdate()
    {
        _updateWindow.ShowOrActivate();
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        _updateWindow.ShowOrActivate();

        // The window only checks on its first show; trigger one here explicitly when no
        // check has completed yet (the manager debounces repeats).
        if (_updateManager.LastCheckUtc is null && _updateWindow.DataContext is UpdateViewModel vm)
        {
            vm.CheckNowCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void Exit()
    {
        Application.Current.Shutdown();
    }

    private void OnUpdateStateChanged(object? sender, EventArgs e)
    {
        _dispatcher.Post(() =>
        {
            OnPropertyChanged(nameof(HasAvailableUpdate));
            OnPropertyChanged(nameof(AvailableUpdateText));
        });
    }
}
