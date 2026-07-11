# Changelog

All notable changes to AiMeter are documented in this file.

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
