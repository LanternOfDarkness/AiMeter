using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AiMeter.Models;
using AiMeter.ViewModels;
using AiMeter.Windowing;

namespace AiMeter.Views;

public partial class WidgetWindow : Window
{
    private readonly WidgetViewModel _viewModel;
    private bool _isHovered;
    private bool _positionApplied;

    /// <summary>
    /// True when the user deliberately hid the widget (via the Hide command). Lets the
    /// self-heal timer distinguish "user wants it gone" from "something external made it
    /// disappear" (e.g. the Snipping Tool's own topmost overlay hiding us mid-screenshot).
    /// </summary>
    public bool IsUserHidden { get; internal set; }

    /// <summary>User-initiated hide: marks the widget as intentionally hidden.</summary>
    public void HideByUser()
    {
        IsUserHidden = true;
        Hide();
    }

    // Self-heal: layered on top of the event-driven foreground hook, because the hook can
    // only re-assert topmost while already visible — it cannot recover a window that some
    // external process hid. A low-frequency poll re-shows and re-asserts in that case.
    private DispatcherTimer? _selfHealTimer;

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

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

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
        Closed += (s, e) =>
        {
            _selfHealTimer?.Stop();
            RemoveForegroundHook();
        };

        // Opacity depends on config (enabled/level) which can change live from the
        // settings slider, plus the transient hover state.
        _viewModel.Config.PropertyChanged += Config_PropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInitialPosition();
        ApplyOpacity();
        InstallForegroundHook();
        StartSelfHealTimer();
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

    private void StartSelfHealTimer()
    {
        _selfHealTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _selfHealTimer.Tick += OnSelfHealTick;
        _selfHealTimer.Start();
    }

    private void OnSelfHealTick(object? sender, EventArgs e)
    {
        if (IsUserHidden) return;

        if (!IsVisible)
        {
            Show();
        }

        ReassertTopmost();
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
    /// Clamps the current top-left into the work area of the monitor the widget is
    /// currently on, without re-anchoring, so the widget keeps the position the user
    /// dragged it to even as its size changes. Clamping against the window's own monitor
    /// (not just the primary) also stops a widget on a secondary display from snapping back
    /// to the primary monitor on every size change (layout toggle, metric count change).
    /// </summary>
    private void KeepOnScreen()
    {
        if (!_positionApplied) return;

        var bounds = GetWorkAreaForWindow();
        var (left, top) = ScreenMath.ClampToBounds(
            this.Left, this.Top, this.ActualWidth, this.ActualHeight,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height);

        this.Left = left;
        this.Top = top;
    }

    /// <summary>
    /// Returns the work area (in WPF DIPs) of the monitor the window currently overlaps,
    /// via Win32 MonitorFromWindow/GetMonitorInfo so we don't depend on WinForms (which
    /// would clash with WPF's global usings). Falls back to the primary work area until
    /// the window has an HWND.
    /// </summary>
    private System.Windows.Rect GetWorkAreaForWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return SystemParameters.WorkArea;

        var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return SystemParameters.WorkArea;

        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref info)) return SystemParameters.WorkArea;

        var scale = GetDpiScale();
        return new System.Windows.Rect(
            info.rcWork.Left / scale.x,
            info.rcWork.Top / scale.y,
            (info.rcWork.Right - info.rcWork.Left) / scale.x,
            (info.rcWork.Bottom - info.rcWork.Top) / scale.y);
    }

    private (double x, double y) GetDpiScale()
    {
        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
        return m.HasValue ? (m.Value.M11, m.Value.M22) : (1.0, 1.0);
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
            KeepOnScreen(); // drag can leave the widget partially off-screen; clamp before persisting.
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
