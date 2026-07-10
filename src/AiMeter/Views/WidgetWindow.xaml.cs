using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AiMeter.Models;
using AiMeter.ViewModels;

namespace AiMeter.Views;

public partial class WidgetWindow : Window
{
    private readonly WidgetViewModel _viewModel;
    private bool _isHovered;
    private bool _positionApplied;

    // Foreground-change hook: re-assert topmost whenever any window (incl. the taskbar)
    // becomes foreground, so the widget never gets buried by the shell. Event-driven, so
    // it costs nothing between focus changes - no polling timer.
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private WinEventDelegate? _winEventProc;
    private IntPtr _winEventHook;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public WidgetWindow(WidgetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        // SizeToContent means the window is still small (no metrics loaded yet) the
        // first time it's shown, then grows once data/format changes arrive - so
        // positioning has to react to size changes, not just the first Loaded.
        Loaded += OnLoaded;
        SizeChanged += (s, e) => KeepOnScreen();
        Closed += (s, e) => RemoveForegroundHook();

        // Opacity depends on config (enabled/level) which can change live from the
        // settings slider, plus the transient hover state.
        _viewModel.Config.PropertyChanged += Config_PropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInitialPosition();
        ApplyOpacity();
        InstallForegroundHook();
    }

    private void InstallForegroundHook()
    {
        if (_winEventHook != IntPtr.Zero) return;

        // Keep the delegate in a field so the GC doesn't collect it while the hook lives.
        _winEventProc = OnForegroundChanged;
        _winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void RemoveForegroundHook()
    {
        if (_winEventHook == IntPtr.Zero) return;
        UnhookWinEvent(_winEventHook);
        _winEventHook = IntPtr.Zero;
        _winEventProc = null;
    }

    private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        => ReassertTopmost();

    /// <summary>
    /// Restores the saved position if the user has moved the widget before; otherwise
    /// falls back to the bottom-right taskbar corner. Only runs once so a later
    /// SizeChanged (e.g. a layout toggle) never snaps the widget back to the corner.
    /// </summary>
    private void ApplyInitialPosition()
    {
        if (_positionApplied) return;
        _positionApplied = true;

        var config = _viewModel.Config;
        if (config.WidgetLeft is double left && config.WidgetTop is double top)
        {
            this.Left = left;
            this.Top = top;
            KeepOnScreen();
        }
        else
        {
            AnchorToBottomRight();
        }
    }

    private void AnchorToBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        this.Left = workArea.Right - this.ActualWidth - 20;
        this.Top = workArea.Bottom - this.ActualHeight - 20;
    }

    /// <summary>
    /// Clamps the current top-left into the work area without re-anchoring, so the
    /// widget keeps the position the user dragged it to even as its size changes.
    /// </summary>
    private void KeepOnScreen()
    {
        if (!_positionApplied) return;

        var workArea = SystemParameters.WorkArea;
        var maxLeft = workArea.Right - this.ActualWidth;
        var maxTop = workArea.Bottom - this.ActualHeight;

        this.Left = System.Math.Max(workArea.Left, System.Math.Min(this.Left, maxLeft));
        this.Top = System.Math.Max(workArea.Top, System.Math.Min(this.Top, maxTop));
    }

    private void ReassertTopmost()
    {
        if (!IsVisible) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Push back to the top of the z-order without activating (so we never steal focus
        // from whatever the user just clicked, including the taskbar).
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
            _viewModel.PersistPosition(this.Left, this.Top);
        }
    }

    private void RootBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        _isHovered = true;
        ApplyOpacity();
    }

    private void RootBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        _isHovered = false;
        ApplyOpacity();
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppConfig.WidgetOpacity)
            or nameof(AppConfig.OpacityEnabled)
            or nameof(AppConfig.HoverOpacity))
        {
            ApplyOpacity();
        }
    }

    private void ApplyOpacity()
    {
        var config = _viewModel.Config;
        if (!config.OpacityEnabled)
        {
            RootBorder.Opacity = 1.0;
            return;
        }

        RootBorder.Opacity = _isHovered ? config.HoverOpacity : config.WidgetOpacity;
    }
}
