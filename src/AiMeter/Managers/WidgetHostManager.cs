using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using AiMeter.Models;
using AiMeter.ViewModels;
using AiMeter.Views;

namespace AiMeter.Managers;

/// <summary>
/// Owns the lifetime switch between the free-floating <see cref="WidgetWindow"/> and the
/// taskbar-hosted <see cref="Views.DeskbandWidgetWindow"/>. Exactly one of the two is live at
/// any time: in Deskband mode the floating window is hidden via <c>HideByUser()</c> (which its
/// self-heal timer respects) and the deskband host is created; every other mode disposes the
/// deskband host and shows the floating window again.
/// </summary>
public sealed class WidgetHostManager : IDisposable
{
    private readonly WidgetWindow _floating;
    private readonly WidgetViewModel _viewModel;
    private readonly ISettingsManager _settingsManager;
    private DeskbandWidgetWindow? _deskband;

    // Guards the async mode-switch / recreation paths against re-entrancy: both run on the UI
    // thread but await the library's layout-ready task, during which another config change or
    // recreation event could otherwise start a second overlapping switch.
    private bool _switching;
    private bool _disposed;

    public WidgetHostManager(WidgetWindow floating, WidgetViewModel viewModel, ISettingsManager settingsManager)
    {
        _floating = floating;
        _viewModel = viewModel;
        _settingsManager = settingsManager;
        _settingsManager.Current.PropertyChanged += OnConfigChanged;
    }

    /// <summary>Applies the persisted layout mode at startup.</summary>
    public void Initialize() => ApplyMode();

    /// <summary>
    /// Tray "Show Widget". In Deskband mode the library/taskbar governs visibility, so there
    /// is no floating window to re-show and this is intentionally a no-op.
    /// </summary>
    public void ShowWidget()
    {
        if (EffectiveMode() == WidgetLayoutMode.Deskband) return;

        // Re-showing from the tray is intentional, so clear any prior user-hide.
        _floating.IsUserHidden = false;
        _floating.Show();
        if (_floating.WindowState == WindowState.Minimized)
        {
            _floating.WindowState = WindowState.Normal;
        }
        _floating.Activate();
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppConfig.WidgetLayoutMode))
        {
            ApplyMode();
        }
    }

    /// <summary>
    /// The mode actually in force. Deskband requires 24H2+; if the config asks for it on an
    /// older OS (e.g. a settings file copied from another machine), fall back to Taskbar.
    /// </summary>
    private WidgetLayoutMode EffectiveMode()
    {
        var config = _settingsManager.Current;
        return config.WidgetLayoutMode == WidgetLayoutMode.Deskband && !config.IsDeskbandSupported
            ? WidgetLayoutMode.Taskbar
            : config.WidgetLayoutMode;
    }

    private async void ApplyMode()
    {
        if (_disposed || _switching) return;
        _switching = true;
        try
        {
            if (EffectiveMode() == WidgetLayoutMode.Deskband)
            {
                // Hide through HideByUser so the floating window's self-heal timer doesn't
                // resurrect it while the deskband host is the visible representation.
                _floating.HideByUser();

                if (_deskband is null)
                {
                    await CreateAndAttachDeskbandAsync();
                }
            }
            else
            {
                DisposeDeskband();
                _floating.IsUserHidden = false;
                _floating.Show();
            }
        }
        finally
        {
            _switching = false;
        }
    }

    private async Task CreateAndAttachDeskbandAsync()
    {
        var window = new DeskbandWidgetWindow(_viewModel);
        window.RecreationRequired += OnRecreationRequired;
        _deskband = window;
        await window.PrepareAsync();
        window.Show();
    }

    /// <summary>
    /// The library recreated the taskbar window (e.g. Explorer restarted). Rebuild the host
    /// against the new taskbar HWND when we're still in Deskband mode.
    /// </summary>
    private async void OnRecreationRequired(object? sender, EventArgs e)
    {
        if (_disposed || _switching) return;
        _switching = true;
        try
        {
            DisposeDeskband();
            if (EffectiveMode() == WidgetLayoutMode.Deskband)
            {
                await CreateAndAttachDeskbandAsync();
            }
        }
        finally
        {
            _switching = false;
        }
    }

    private void DisposeDeskband()
    {
        if (_deskband is null) return;

        _deskband.RecreationRequired -= OnRecreationRequired;
        _deskband.DisposeHost();
        _deskband.Close();
        _deskband = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _settingsManager.Current.PropertyChanged -= OnConfigChanged;
        DisposeDeskband();
    }
}
