# Changelog

All notable changes to AiMeter are documented in this file.

## [Unreleased]

### Added
- **Account status notes** — the OpenCode row in Settings ▸ Accounts now says why no bars
  appear, e.g. "Logged In · No active Go subscription" (orange), or "Usage unavailable (see
  log)", instead of a plain green "Logged In" with nothing on the widget.

### Fixed
- **OpenCode usage stopped loading** — opencode.ai moved its dashboard to the new OpenCode
  Console, and `/go` became a public marketing page, so the provider never found a workspace
  and reported nothing. Usage is now read from the console's own `/api/go/status` response
  (captured from the logged-in page, so the site applies its own auth), with a fallback to the
  old per-workspace page for plans that only exist there. If the console's selected org has
  no plan, the user's other orgs are checked and the one with the plan is kept selected.
- **OpenCode login reported success without logging in** — the login window accepted any
  cookie as proof. It now logs into the Console first (success only once a console page past
  the login screen is reached), then the original opencode.ai sign-in for the legacy page.
- **Metrics from a provider logged into later stayed hidden** — once the first poll filled the
  metric selection (e.g. with Claude's metrics), a newly logged-in provider's metrics were
  left unchecked, off the widget. Metrics seen for the first time are now selected once;
  ones you've unchecked stay unchecked. Existing settings keep their choices per provider.
- **Login/logout now refreshes immediately** instead of waiting up to a full polling interval.

## [1.2.0] - 2026-07-27

### Added
- **Compact-mode customization**: the bars layout is now configurable in Settings ▸ Appearance.
  - **Multi-column bars** — set how many bars stack in one column; once a column is full the
    remaining metrics wrap into a second column, then a third, and the widget grows sideways.
  - **Bar width** and **cell gap** sliders to make each row wider/narrower and looser/tighter.
  - **Column gap** slider controlling the spacing between wrapped bar columns.
- **Font size** setting — scales the widget text (Compact rows, and Taskbar column
  labels/numbers within a clamp so the docked column can't overflow).
- **Hide numeric values** toggle — drops the always-visible % and reset text in both layouts,
  leaving just the label/code and the colored bar; the full numbers stay available on hover.
- **Per-metric custom label** — override a metric's displayed name in Compact mode from
  Settings ▸ Metrics Shown (Taskbar keeps its auto 3-letter code).
- **Per-metric color** — pick a bar color per metric (or "Auto" to keep the quota-threshold
  palette) from Settings ▸ Metrics Shown; applies in both layouts.
- **Claude Extra Usage money metric** — tracks Claude's pay-as-you-go spend limit ("Claude Extra
  Usage") with 30-day reset calculation and `CEU` taskbar code. Shows `$used / $limit` (with a
  proportional bar) on the standard "Limited" plan; on an "Unlimited" plan (no monthly cap) it
  shows the prepaid balance left instead of a meaningless percentage, with no bar.
- **Game mode**: while a borderless-fullscreen app (a game) is in the foreground on the
  widget's own monitor, the widget automatically becomes click-through — it stays visible
  and on top, but the mouse passes straight through it to the game underneath instead of
  being captured by the widget. Reverts the instant a normal window regains focus, driven by
  the same foreground-change hook that already re-asserts topmost.

### Changed
- **Settings window reorganized into tabs** (Widget · Compact · Metrics · General) so the
  growing list of options no longer stacks into one long scroll; the window is now a more
  focused size with dark-themed tabbed navigation.

### Fixed
- **Claude session status persistence** — fixed issue where background API requests executed against
  a stale `/login` page context returned `401 Unauthorized` and prematurely wiped active login status.
- **Dynamic custom label width** — Compact mode metric row labels now automatically resize to fit
  configured custom label text instead of clipping against a hardcoded 76px boundary.
- **Claude Extra Usage showing the wrong remaining %** — `monthly_limit`/`used_credits` are minor
  units scaled by the payload's `decimal_places` (not already dollars), and `spend.limit` is an
  `{amount_minor, exponent}` object, not a bare number; both were misread, so a computed fallback
  then silently overwrote the correct server-reported percentage. A 76%-used account was showing
  99% remaining instead of ~24%.

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
