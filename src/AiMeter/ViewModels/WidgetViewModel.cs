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

    /// <summary>
    /// True once the first poll has completed and no provider has an active session at all.
    /// Distinct from a fetch error on a logged-in provider (that keeps showing its
    /// "Error Fetching" tile) - this is specifically "nothing to show, go log in".
    /// </summary>
    public bool IsEmptyState => HasFetchedOnce && !Config.HasClaudeSession && !Config.HasOpenCodeSession;

    public bool ShowSkeleton => !HasFetchedOnce || (Metrics.Count == 0 && !IsEmptyState);

    public bool ShowRealContent => HasFetchedOnce && (Metrics.Count > 0 || IsEmptyState);

    /// <summary>
    /// Fixed pixel height of a single Compact bar row, derived from the configured font size so
    /// the multi-column wrap math is exact. The row template pins its root to this height (no
    /// outer margin), so a column of N rows is exactly N × this tall.
    /// </summary>
    public double CompactRowHeight => Math.Ceiling(Config.WidgetFontSize * 1.5) + 4;

    /// <summary>
    /// Height cap the Compact WrapPanel wraps at: once a column reaches BarsPerColumn rows it
    /// starts a new column. 0 bars-per-column means a single unbounded column (never wraps).
    /// The small epsilon absorbs floating-point rounding so exactly N rows fit per column.
    /// </summary>
    public double CompactColumnMaxHeight => Config.BarsPerColumn > 0
        ? Config.BarsPerColumn * CompactRowHeight + 0.5
        : double.PositiveInfinity;

    [ObservableProperty]
    private DateTime _now = DateTime.Now;

    public WidgetViewModel(IProviderManager providerManager, ISettingsManager settingsManager, SettingsWindow settingsWindow)
    {
        _providerManager = providerManager;
        _settingsManager = settingsManager;
        _settingsWindow = settingsWindow;

        _providerManager.Metrics.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(ShowSkeleton));
            OnPropertyChanged(nameof(ShowRealContent));
        };

        // Relay HasFetchedOnce changes from the manager so the widget's skeleton/real
        // toggle updates via binding (the manager owns the source of truth).
        _providerManager.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(HasFetchedOnce))
            {
                OnPropertyChanged(nameof(HasFetchedOnce));
                OnPropertyChanged(nameof(IsEmptyState));
                OnPropertyChanged(nameof(ShowSkeleton));
                OnPropertyChanged(nameof(ShowRealContent));
            }
        };

        // IsEmptyState also depends on the session flags, which live on Config (not this VM),
        // so neither is an automatic [ObservableProperty] dependency - relay explicitly.
        Config.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(AppConfig.HasClaudeSession) or nameof(AppConfig.HasOpenCodeSession))
            {
                OnPropertyChanged(nameof(IsEmptyState));
                OnPropertyChanged(nameof(ShowSkeleton));
                OnPropertyChanged(nameof(ShowRealContent));
            }

            // The Compact column layout is computed from these two settings; relay so the
            // WrapPanel's row height / wrap cap re-bind when the user changes them in Settings.
            if (e.PropertyName is nameof(AppConfig.WidgetFontSize) or nameof(AppConfig.BarsPerColumn))
            {
                OnPropertyChanged(nameof(CompactRowHeight));
                OnPropertyChanged(nameof(CompactColumnMaxHeight));
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
        Config.WidgetLayoutMode = Config.WidgetLayoutMode == WidgetLayoutMode.Compact
            ? WidgetLayoutMode.Taskbar
            : WidgetLayoutMode.Compact;
        _settingsManager.Save();
    }

    public void PersistPosition(double left, double top)
    {
        Config.WidgetLeft = left;

        // Taskbar mode is a dock, not a free-floating window - Top is always recomputed from
        // the work area (see WidgetWindow.KeepOnScreen), never restored from a saved value.
        if (Config.WidgetLayoutMode != WidgetLayoutMode.Taskbar)
        {
            Config.WidgetTop = top;
        }

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
