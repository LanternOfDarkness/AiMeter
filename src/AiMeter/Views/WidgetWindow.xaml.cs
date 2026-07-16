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
    private HwndSource? _hwndSource;

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

    // Self-heal: layered on top of the event-driven foreground hook below, because that hook
    // can only re-assert topmost while already visible - it cannot recover a window that some
    // external process hid (e.g. the Snipping Tool's own topmost overlay hiding us mid-shot).
    // A low-frequency poll re-shows and re-asserts in that case.
    private DispatcherTimer? _selfHealTimer;

    // Foreground-change hook: re-assert topmost whenever any window (incl. the taskbar)
    // becomes foreground. This is the mechanism that actually stops the flicker - when the
    // taskbar (itself topmost) is clicked, Explorer reorders *it* to the front of the topmost
    // band; that reorder happens on the taskbar's own HWND and never sends our window any
    // WM_WINDOWPOSCHANGING message, so a hook on our own window cannot see or prevent it. We
    // have to react to the foreground-change event and immediately push ourselves back above
    // it. Event-driven, so it costs nothing between focus changes - no polling required.
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private WinEventDelegate? _winEventProc;
    private IntPtr _winEventHook;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int WM_WINDOWPOSCHANGING = 0x0046;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    // Fallback Taskbar-mode height when the taskbar thickness can't be detected for the
    // widget's current monitor (see GetTaskbarHeight) - robust edge detection (top/left/right
    // taskbars) is a follow-up; this covers the common bottom-taskbar case directly.
    private const double DefaultTaskbarHeight = 40;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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
            SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        };

        // Opacity depends on config (enabled/level) which can change live from the
        // settings slider, plus the transient hover state.
        _viewModel.Config.PropertyChanged += Config_PropertyChanged;
    }

    /// <summary>
    /// Sets up the window as a true always-on-top, non-activating tool window and installs a
    /// WM_WINDOWPOSCHANGING hook that forces any z-order change targeting *our own* window
    /// back to HWND_TOPMOST before it takes effect. This catches direct attempts to move us
    /// out of the topmost band, but it can't see a sibling topmost window (like the taskbar)
    /// reordering itself above us - that reorder never touches our HWND. The foreground-change
    /// hook installed alongside it (see InstallForegroundHook) is what actually handles that
    /// case, by reacting the instant the taskbar becomes foreground.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        InstallForegroundHook();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            // When the user has turned off "show over fullscreen", yield to a fullscreen app
            // (a game etc.) instead of forcing ourselves back above its own topmost surface.
            if (_viewModel.Config.ShowOverFullscreen || !IsForegroundWindowFullscreen())
            {
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                pos.hwndInsertAfter = HWND_TOPMOST;
                pos.flags &= ~SWP_NOZORDER;
                Marshal.StructureToPtr(pos, lParam, true);
            }
        }

        return IntPtr.Zero;
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

    private void ReassertTopmost()
    {
        if (!IsVisible) return;

        // When the user has turned off "show over fullscreen", yield to a fullscreen app (a
        // game etc.): skip the re-assert so its own topmost surface stays above the widget.
        if (!_viewModel.Config.ShowOverFullscreen && IsForegroundWindowFullscreen()) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Push back to the top of the z-order without activating (so we never steal focus
        // from whatever the user just clicked, including the taskbar).
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyModeBehavior();
        ApplyInitialPosition();
        ApplyOpacity();
        StartSelfHealTimer();

        // Covers resolution/DPI/taskbar-thickness changes on the current monitor - Taskbar
        // mode's docked height and position both depend on the work area.
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
    }

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
        {
            Dispatcher.BeginInvoke(ApplyModeBehavior);
        }
    }

    /// <summary>
    /// Applies the current <see cref="WidgetLayoutMode"/>'s window behavior: Taskbar mode
    /// gets a fixed height (docked to the physical bottom of the screen, overlapping the
    /// taskbar - see <see cref="KeepOnScreen"/>) with only the width auto-sizing; Compact
    /// mode auto-sizes both.
    /// </summary>
    private void ApplyModeBehavior()
    {
        if (_viewModel.Config.WidgetLayoutMode == WidgetLayoutMode.Taskbar)
        {
            SizeToContent = SizeToContent.Width;
            var height = GetTaskbarHeight();
            Height = height;

            // SizeToContent="Width" still measures content's height with an effectively
            // unconstrained pass and can silently grow the window past the explicit Height
            // above if that content (metric columns, loading skeleton) wants more room -
            // empirically confirmed via diagnostics, not just a documentation assumption.
            // MaxHeight is a hard ceiling WPF's layout system does enforce, so it's what
            // actually keeps the window pinned to the detected taskbar thickness.
            MaxHeight = height;
        }
        else if (SizeToContent != SizeToContent.WidthAndHeight)
        {
            ClearValue(HeightProperty);
            ClearValue(MaxHeightProperty);
            SizeToContent = SizeToContent.WidthAndHeight;
        }

        KeepOnScreen();
    }

    /// <summary>
    /// Detects the taskbar's thickness as MonitorBounds.Bottom - WorkArea.Bottom for the
    /// widget's own monitor, so the docked widget matches it exactly. Only the bottom edge
    /// is handled (the common case); a taskbar docked to the top/left/right falls back to a
    /// fixed default rather than attempting full edge detection.
    /// </summary>
    private double GetTaskbarHeight()
    {
        var (monitor, work) = GetMonitorAndWorkBoundsForWindow();
        var thickness = monitor.Bottom - work.Bottom;
        return thickness > 0 ? Math.Max(24, thickness) : DefaultTaskbarHeight;
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

    /// <summary>
    /// Restores the saved position if the user has moved the widget before; otherwise
    /// falls back to the bottom-right taskbar corner (Compact) or horizontally centered
    /// (Taskbar). Only runs once so a later SizeChanged (e.g. a layout toggle) never snaps
    /// the widget back. Taskbar mode never restores a saved Top - it's always docked - so
    /// only WidgetLeft is meaningful there (see WidgetViewModel.PersistPosition).
    /// </summary>
    private void ApplyInitialPosition()
    {
        if (_positionApplied) return;
        _positionApplied = true;

        var config = _viewModel.Config;

        if (config.WidgetLayoutMode == WidgetLayoutMode.Taskbar)
        {
            var (_, work) = GetMonitorAndWorkBoundsForWindow();
            this.Left = config.WidgetLeft ?? work.Left + (work.Width - this.ActualWidth) / 2;
            KeepOnScreen();
            return;
        }

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
    /// Re-anchors the widget after a size/position/mode change. In Taskbar mode this docks
    /// flush to the physical bottom edge of the monitor - deliberately overlapping the
    /// taskbar rather than floating just above it, matching Compact mode's existing "sits
    /// over the taskbar" behavior. <c>Top</c> is actively re-forced back to the dock position
    /// (not merely left alone), since <c>DragMove()</c> moves both axes and can't be
    /// constrained mid-drag — and <c>Left</c> is clamped horizontally. In Compact mode it
    /// clamps the current top-left into the full bounds of the widget's own monitor, without
    /// re-anchoring, so the widget keeps the position the user dragged it to even as its size
    /// changes. Clamping against the full monitor (not its work area) deliberately lets the
    /// Compact widget sit over the taskbar too, while still stopping at the physical screen
    /// edge; clamping against the window's own monitor (not just the primary) also stops a
    /// widget on a secondary display from snapping back to the primary on every size change.
    /// </summary>
    private void KeepOnScreen()
    {
        if (!_positionApplied) return;

        var (monitor, _) = GetMonitorAndWorkBoundsForWindow();

        if (_viewModel.Config.WidgetLayoutMode == WidgetLayoutMode.Taskbar)
        {
            this.Top = monitor.Bottom - this.ActualHeight;

            var maxDockLeft = monitor.Left + Math.Max(0, monitor.Width - this.ActualWidth);
            this.Left = Math.Clamp(this.Left, monitor.Left, maxDockLeft);
            return;
        }

        // Clamp against the visible border, not the window's full size: RootBorder reserves
        // a transparent Margin for the drop shadow's blur, so clamping the whole window to
        // the monitor left that margin's worth of empty screen between the visible edge and
        // the true screen edge - the widget could never be dragged flush against it.
        var margin = RootBorder.Margin;
        var visibleWidth = this.ActualWidth - margin.Left - margin.Right;
        var visibleHeight = this.ActualHeight - margin.Top - margin.Bottom;

        var (visibleLeft, visibleTop) = ScreenMath.ClampToBounds(
            this.Left + margin.Left, this.Top + margin.Top, visibleWidth, visibleHeight,
            monitor.Left, monitor.Top, monitor.Width, monitor.Height);

        this.Left = visibleLeft - margin.Left;
        this.Top = visibleTop - margin.Top;
    }

    /// <summary>
    /// Returns both the full monitor bounds and the work-area bounds (in WPF DIPs) for the
    /// monitor the window currently overlaps, via Win32 MonitorFromWindow/GetMonitorInfo so
    /// we don't depend on WinForms (which would clash with WPF's global usings) or
    /// <see cref="SystemParameters.WorkArea"/> (which is primary-monitor-only and so can't
    /// drive Taskbar-mode docking on a secondary display). Falls back to the primary screen
    /// bounds until the window has an HWND.
    /// </summary>
    private (System.Windows.Rect Monitor, System.Windows.Rect Work) GetMonitorAndWorkBoundsForWindow()
    {
        var primaryMonitor = new System.Windows.Rect(
            0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        var primaryWork = SystemParameters.WorkArea;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return (primaryMonitor, primaryWork);

        var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return (primaryMonitor, primaryWork);

        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref info)) return (primaryMonitor, primaryWork);

        var scale = GetDpiScale();
        var monitor = new System.Windows.Rect(
            info.rcMonitor.Left / scale.x,
            info.rcMonitor.Top / scale.y,
            (info.rcMonitor.Right - info.rcMonitor.Left) / scale.x,
            (info.rcMonitor.Bottom - info.rcMonitor.Top) / scale.y);
        var work = new System.Windows.Rect(
            info.rcWork.Left / scale.x,
            info.rcWork.Top / scale.y,
            (info.rcWork.Right - info.rcWork.Left) / scale.x,
            (info.rcWork.Bottom - info.rcWork.Top) / scale.y);

        return (monitor, work);
    }

    private (double x, double y) GetDpiScale()
    {
        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
        return m.HasValue ? (m.Value.M11, m.Value.M22) : (1.0, 1.0);
    }

    /// <summary>
    /// True when the foreground window covers an entire monitor — a fullscreen game/app, as
    /// opposed to a merely maximized window (which stops at the work area, leaving the taskbar).
    /// Lets the widget step aside for fullscreen apps when "show over fullscreen" is off.
    /// </summary>
    private bool IsForegroundWindowFullscreen()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == new WindowInteropHelper(this).Handle) return false;
        if (!GetWindowRect(fg, out var wr)) return false;

        var hMonitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return false;

        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref info)) return false;

        var m = info.rcMonitor;
        return wr.Left <= m.Left && wr.Top <= m.Top && wr.Right >= m.Right && wr.Bottom >= m.Bottom;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            // DragMove() moves both axes and can't be constrained mid-drag, so in Taskbar
            // mode the vertical component it applied is undone here: KeepOnScreen() actively
            // re-forces Top back to the dock position (not merely skipping it) and clamps
            // Left horizontally. In Compact mode it just clamps both axes as before.
            DragMove();
            KeepOnScreen();
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

    /// <summary>
    /// Empty-state click routes to Settings instead of starting a window drag. Must handle
    /// the event (not just execute the command) because the window-level
    /// Window_MouseLeftButtonDown handler above would otherwise also fire on the same click
    /// and start a DragMove - a click on this surface is a click, not a drag.
    /// </summary>
    private void EmptyState_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        _viewModel.OpenSettingsCommand.Execute(null);
        e.Handled = true;
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppConfig.WidgetOpacity)
            or nameof(AppConfig.OpacityEnabled)
            or nameof(AppConfig.HoverOpacity))
        {
            ApplyOpacity();
        }
        else if (e.PropertyName == nameof(AppConfig.WidgetLayoutMode))
        {
            // Swaps SizeToContent/fixed-Height and re-anchors (docks in Taskbar, clamps in
            // Compact) for the newly selected mode.
            ApplyModeBehavior();
        }
    }

    private void ApplyOpacity()
    {
        var config = _viewModel.Config;
        // Applied to the content only (not RootBorder) so the hover-revealed controls overlay,
        // a sibling of ContentHost, stays fully opaque even while the metrics dim on hover.
        if (!config.OpacityEnabled)
        {
            ContentHost.Opacity = 1.0;
            return;
        }

        ContentHost.Opacity = _isHovered ? config.HoverOpacity : config.WidgetOpacity;
    }
}
