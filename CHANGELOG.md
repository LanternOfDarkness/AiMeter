# Changelog

All notable changes to AiMeter are documented in this file.

## [1.1.0] - 2026-07-16

### Added
- **Taskbar mode**: a new layout that docks the widget flush against the physical bottom of
  the screen — overlapping the taskbar the same way Compact mode already could, rather than
  floating just above it — sized to match its detected thickness. One narrow vertical-fill
  column per metric shows a 3-letter code, the quota fill, the remaining percentage, a
  time-until-reset sliver, and a compact reset countdown (`3.3h`, `5.2d`) all at rest; the
  full metric name and verbose reset time are one hover away via a dark-themed tooltip.
  Horizontal dragging repositions it; switching back to Compact restores free-floating
  behavior.
- **"Log in required" empty state**: once the first poll completes, if no provider is logged
  in at all, the widget shows a single call-to-action instead of per-provider error tiles;
  click it to open Settings.
- Inno Setup installer (`installer/aimeter.iss`) — per-user, no admin/UAC, installs to
  `%LocalAppData%\Programs\AiMeter`, with optional launch-at-startup/desktop-shortcut tasks
  and automatic .NET 9 Desktop Runtime / WebView2 Runtime prerequisite checks.

### Removed
- The Detailed (circular ring) layout is gone — Compact already showed the same information
  more space-efficiently. `WidgetLayoutMode` now has two values, `Compact` and `Taskbar`;
  the setting is stored under a new JSON key (`LayoutMode`) so upgrading users land on
  `Compact` instead of the old numeric value being reinterpreted as `Taskbar`.

### Fixed
- Alt+Tab no longer shows ghost entries for the Settings/Auth windows: dark-title-bar setup
  now runs on `SourceInitialized` (after `Show()` naturally creates the HWND) instead of
  forcing HWND creation from the constructor.
- Tooltips throughout the widget now use a dark theme matching the rest of the UI instead of
  WPF's default light chrome.

## [1.0.1] - 2026-07-11

### Fixed
- Widget no longer flickers or drops behind the taskbar when the taskbar is clicked, the
  Start menu is opened, or another window is focused. The widget is now a true
  non-activating tool window (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`), stays out of Alt+Tab,
  and re-asserts its topmost position immediately on any foreground change instead of
  relying on a polling timer.
- Text inside the circular progress rings is now properly centered (value and `%` share a
  single `TextBlock` with tuned line-height instead of a `StackPanel`, which visually
  skewed the center).
- Text is crisp instead of blurry/"floating" on 4K and other high-DPI displays: the app now
  declares Per-Monitor-V2 DPI awareness via an application manifest, and the widget uses
  layout rounding and ClearType text rendering.
- The widget can now be dragged flush against the screen edge. The drag-to-screen clamp
  previously measured the whole window, including its transparent drop-shadow margin,
  which always left a ~6px gap at the edge.
- Usage metric updates now flow through data binding instead of replacing/recreating the
  bound UI elements on every poll, removing a source of visual jank on refresh.

## [1.0.0] - 2026-07-11

First public release of AiMeter — a Windows system-tray widget that keeps your AI usage
limits visible at a glance.

### Added
- **Providers**: Claude (claude.ai session/weekly limits) and OpenCode (opencode.ai Go
  rolling/weekly/monthly usage).
- **Floating widget**: Detailed (rings) and Compact (bars) layouts, always-on-top,
  draggable, can sit over the taskbar, hover-reveal controls, live reset countdowns.
- **Settings**: per-provider account login via an embedded browser, metric selection,
  adjustable opacity, alerts (low/exhausted/reset), launch at Windows startup, and staying
  above borderless-fullscreen games.
- System tray app with show/hide widget, open settings, refresh, and exit.
