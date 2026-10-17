using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AiMeter.Models;
using AiMeter.ViewModels;
using Deskband11Lib.Core;
using Deskband11Lib.Wpf;

namespace AiMeter.Views;

/// <summary>
/// A separate window (never <see cref="WidgetWindow"/>) whose root visual is handed to
/// Deskband11Lib so the metric bars render inside the Windows 11 taskbar itself. It shows the
/// Compact look: a two-row-tall column of <see cref="Styles"/> Compact rows per metric,
/// wrapping into extra columns to the right. The library owns its lifetime/re-parenting; this
/// class only builds the host, forwards the recreation signal to
/// <see cref="Managers.WidgetHostManager"/>, and keeps the preferred size in step with the
/// metric count and the relevant layout settings.
/// </summary>
public partial class DeskbandWidgetWindow : Window
{
    // Two Compact rows per column, then wrap to the right - the floating widget's Compact
    // layout uses the same column-first wrap, just with a taller (BarsPerColumn) cap.
    private const int RowsPerColumn = 2;

    // Rough per-column width beyond the bar itself (label + % + reset + cell/column gaps);
    // the library re-measures the real content on layout, so this only seeds the first pass.
    private const double PerColumnChrome = 150;

    private readonly WidgetViewModel _viewModel;
    private readonly TaskbarContentHostOptions _options;
    private TaskbarContentHost? _host;

    /// <summary>Raised when the library reports the taskbar window was recreated, so the
    /// manager can rebuild this host against the new taskbar HWND.</summary>
    public event EventHandler? RecreationRequired;

    public DeskbandWidgetWindow(WidgetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _options = BuildOptions(viewModel);
        Height = _options.PreferredHeight;

        // The preferred size is derived from the metric count and the Compact layout settings,
        // so recompute it (and ask the library to re-measure) when either changes.
        viewModel.Metrics.CollectionChanged += OnLayoutInputsChanged;
        viewModel.Config.PropertyChanged += OnConfigChanged;
    }

    /// <summary>
    /// Creates the content host and attaches it once the taskbar layout is ready. The library
    /// waits for the taskbar itself, so no retry loop is needed here.
    /// </summary>
    public async Task PrepareAsync()
    {
        _host = new TaskbarContentHost(this, DeskbandRoot, _options);
        _host.TaskbarWindowRecreationRequired += (s, e) => RecreationRequired?.Invoke(this, EventArgs.Empty);
        await _host.AttachWhenLayoutReadyAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Metrics.CollectionChanged -= OnLayoutInputsChanged;
        _viewModel.Config.PropertyChanged -= OnConfigChanged;
        DisposeHost();
        base.OnClosed(e);
    }

    /// <summary>Detaches and disposes the library host, if any. Safe to call repeatedly.</summary>
    public void DisposeHost()
    {
        _host?.Dispose();
        _host = null;
    }

    private void OnEmptyState_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // Handle the click so it doesn't also start any drag / hit-testing on the root.
        _viewModel.OpenSettingsCommand.Execute(null);
        e.Handled = true;
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppConfig.WidgetFontSize) or nameof(AppConfig.BarWidth)
            or nameof(AppConfig.CompactCellGap) or nameof(AppConfig.CompactColumnGap)
            or nameof(AppConfig.ShowNumericValues))
        {
            ApplyContentSize();
        }
    }

    private void OnLayoutInputsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyContentSize();

    private void ApplyContentSize()
    {
        _options.PreferredWidth = PreferredWidthForMetrics(_viewModel);
        _options.PreferredHeight = PreferredHeightForMetrics(_viewModel);
        Height = _options.PreferredHeight;
        _host?.NotifyContentSizeChanged();
    }

    /// <summary>
    /// Builds the library options for this widget: width estimated from the metric count (two
    /// rows per column), height matching two Compact rows, and automatic placement in the taskbar.
    /// </summary>
    public static TaskbarContentHostOptions BuildOptions(WidgetViewModel vm) => new()
    {
        PreferredWidth = PreferredWidthForMetrics(vm),
        PreferredHeight = PreferredHeightForMetrics(vm),
        Placement = TaskbarContentPlacement.Auto,
        PreferredMonitorIdentity = 0,
        AnimateLayoutChanges = true,
        HighRefreshRateMode = false,
        TrackTaskbarButtons = true,
        TrackNotificationArea = true,
    };

    private static double PreferredWidthForMetrics(WidgetViewModel vm)
    {
        var columns = Math.Max(1, (int)Math.Ceiling(vm.Metrics.Count / (double)RowsPerColumn));
        return columns * (vm.Config.BarWidth + PerColumnChrome) + 16;
    }

    private static double PreferredHeightForMetrics(WidgetViewModel vm)
        => RowsPerColumn * vm.CompactRowHeight + 4;
}
