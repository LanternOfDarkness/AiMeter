using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AiMeter.Managers;
using AiMeter.Models;
using AiMeter.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiMeter.ViewModels;

public partial class WidgetViewModel : ObservableObject
{
    private readonly IProviderManager _providerManager;
    private readonly ISettingsManager _settingsManager;
    private readonly SettingsWindow _settingsWindow;
    private readonly DispatcherTimer _clockTimer;

    public ObservableCollection<UsageMetric> Metrics => _providerManager.Metrics;
    public AppConfig Config => _settingsManager.Current;
    public bool HasFetchedOnce => _providerManager.HasFetchedOnce;

    [ObservableProperty]
    private DateTime _now = DateTime.Now;

    public WidgetViewModel(IProviderManager providerManager, ISettingsManager settingsManager, SettingsWindow settingsWindow)
    {
        _providerManager = providerManager;
        _settingsManager = settingsManager;
        _settingsWindow = settingsWindow;

        // Relay HasFetchedOnce changes from the manager so the widget's skeleton/real
        // toggle updates via binding (the manager owns the source of truth).
        _providerManager.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(HasFetchedOnce))
            {
                OnPropertyChanged(nameof(HasFetchedOnce));
            }
        };

        // Ticks independently of provider polling so reset countdowns stay live.
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (s, e) => Now = DateTime.Now;
        _clockTimer.Start();
    }

    [RelayCommand]
    private void Hide(object? target)
    {
        // The title-bar button passes the Window directly; the context menu passes its
        // PlacementTarget (the root Border), so resolve the owning window in that case.
        var window = target as Window
            ?? (target is DependencyObject dep ? Window.GetWindow(dep) : null);

        // Route through HideByUser so the self-heal timer knows this was an intentional
        // hide and does not resurrect the widget a few seconds later.
        if (window is WidgetWindow widgetWindow)
        {
            widgetWindow.HideByUser();
        }
        else
        {
            window?.Hide();
        }
    }

    [RelayCommand]
    private void ToggleLayout()
    {
        Config.WidgetLayoutMode = Config.WidgetLayoutMode == WidgetLayoutMode.Detailed
            ? WidgetLayoutMode.Compact
            : WidgetLayoutMode.Detailed;
        _settingsManager.Save();
    }

    public void PersistPosition(double left, double top)
    {
        Config.WidgetLeft = left;
        Config.WidgetTop = top;
        _settingsManager.Save();
    }

    [RelayCommand]
    private async Task Refresh() => await _providerManager.RefreshAsync();

    [RelayCommand]
    private void OpenSettings()
    {
        _settingsWindow.ShowOrActivate();
    }
}
