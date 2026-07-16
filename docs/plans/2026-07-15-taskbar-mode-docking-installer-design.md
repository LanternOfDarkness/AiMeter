# Taskbar mode, bottom-docking, installer & polish — design

**Date:** 2026-07-15 (verified against codebase & corrected 2026-07-16)
**Status:** Approved (sections 1–4 approved by user; sections 5–6 filled from gathered decisions with recommended defaults)

## Overview

Six work items that take the widget from a two-mode floating ring/bar app to a single
streamlined compact experience plus a new taskbar-docked mode, fix Alt+Tab ghost windows,
add a clear empty-state, and ship an Inno Setup installer.

## Background & decisions

- The existing Detailed (circular ring) layout is being removed: it's broken, space-hungry,
  and conveys no information the Compact bar layout doesn't already show.
- A new "Taskbar" mode is a docked widget pinned above the taskbar at taskbar height, with
  one thin vertical-fill column per metric, growing horizontally as providers/metrics are
  added — designed to live unobtrusively on the taskbar across many tracked models.
- Taskbar mode and "bind to screen bottom" are **one combined feature** (selecting Taskbar
  mode docks the widget to the bottom of the work area). Compact mode stays free-floating.
- An empty ("Log in required") state replaces the per-provider "Auth Required" tile when no
  provider is logged in at all, and doubles as a call-to-action (click → Settings).
- A simple Inno Setup installer replaces the plain zip distribution.
- Approach A (from three proposed): layout-mode-driven window behavior — one window, one set
  of hooks; Taskbar mode swaps both the visual template and the window/dock behavior.

## Sequencing

| # | Work item | Depends on | Risk |
|---|-----------|-----------|------|
| 1 | Fix Alt+Tab ghost windows (DarkTitleBar) | none | low — isolated |
| 2 | Remove circle/Detailed mode | none | low — deletion + enum trim |
| 3 | Empty-state "Log in required" | 2 | low — mostly exists |
| 4 | Taskbar mode + bottom docking | 2, 3 | medium — new template + dock logic |
| 5 | Inno Setup installer | 1–4 | low — isolated script |
| 6 | README/docs update | all | low |

---

## Section 1 — Fix Alt+Tab ghost windows

### Root cause

`DarkTitleBar.Apply()` (`src/AiMeter/Interop/DarkTitleBar.cs:16`) calls
`WindowInteropHelper.EnsureHandle()` in every window's constructor, forcing HWND creation
before `Show()`. The DI chain eagerly constructs `SettingsWindow` at startup
(`App.OnStartup` → `GetRequiredService<WidgetWindow>()` → `WidgetViewModel` ctor →
`SettingsWindow` singleton → `DarkTitleBar.Apply(this)`), so its HWND is created and hidden
without `WS_EX_TOOLWINDOW` — hidden HWNDs without that style still appear in Alt+Tab. The
widget itself applies `WS_EX_TOOLWINDOW` in `OnSourceInitialized`, so it doesn't leak; the
ghosts are the other constructed-but-not-shown windows.

### Fix

Move the DWM attribute call off the eager-construction path:

- `DarkTitleBar.Apply(Window)` → split into `ApplyToHwnd(IntPtr)`, drop `EnsureHandle()`.
- Each window calls it from `SourceInitialized` (fires once, after the HWND is naturally
  created on `Show()`) instead of the constructor.
  - `SettingsWindow` / `AuthWindow` / `OpenCodeAuthWindow` hook `SourceInitialized` in the
    ctor (or override `OnSourceInitialized`).
  - `WidgetWindow` does **not** call `DarkTitleBar` today and doesn't need to — it's
    borderless (`WindowStyle="None"`), there's no title bar to darken. No change there.

The DWM attribute is per-HWND and persists once set; applying it post-creation is identical
to applying it pre-creation. No window gets a ghost HWND at startup. Auth windows are
transient (constructed only on login click, shown immediately) so they never linger hidden.

**Verification note:** whether a hidden, never-shown HWND appears in Alt+Tab is empirical
(known WPF ghost-window phenomenon, but Alt+Tab heuristics vary by Windows build). The fix is
correct and harmless regardless; after implementing, confirm the ghosts are actually gone.

### Edge case — `H.NotifyIcon.ForceCreate()`

The tray icon's hidden message window (created by `ForceCreate()` at `App.xaml.cs:78`) uses
`WS_EX_TOOLWINDOW` internally in H.NotifyIcon.Wpf, so it doesn't appear in Alt+Tab. No change
needed there.

### Files

- `src/AiMeter/Interop/DarkTitleBar.cs` — remove `EnsureHandle()`, expose `ApplyToHwnd(IntPtr)`.
- `src/AiMeter/Views/SettingsWindow.xaml.cs` — hook `SourceInitialized`.
- `src/AiMeter/Views/AuthWindow.xaml.cs` — hook `SourceInitialized`.
- `src/AiMeter/Views/OpenCodeAuthWindow.xaml.cs` — hook `SourceInitialized`.

---

## Section 2 — Remove circle/Detailed mode

Full removal (option (a) from the design discussion): delete the enum, the toggle, and all
Detailed-mode scaffolding. Cleaner than leaving a one-mode toggle as dead UI.

### Changes

1. **`Models/WidgetLayoutMode.cs`** — delete the enum. (Reintroduced with new values in
   Section 4; see the migration note below.)
2. **`Models/AppConfig.cs`** — delete the `WidgetLayoutMode` property.
3. **`Views/WidgetWindow.xaml`**:
   - Delete the Detailed skeleton `StackPanel` with `<Ellipse>` (lines 71–77).
   - Delete the Detailed `ItemsControl` (lines 80–88).
   - The Compact `ItemsControl` (lines 91–132) becomes the only content; drop its
     `Visibility` binding.
   - Delete the `⇅` layout-toggle button (line 155) and the "Switch Layout" context-menu
     item (lines 56–57).
   - **Keep** the `EnumToVisibilityConverter` resource (line 26) and
     `InverseBooleanToVisibilityConverter` (line 29) even though Section 2 removes their last
     users — Section 3's loading skeleton needs the inverse-bool converter and Section 4's
     Compact/Taskbar templates need the enum converter. Deleting then re-adding is churn.
4. **`Views/WidgetWindow.xaml.cs`**:
   - Delete `ApplyMinSizeForLayout()` (lines 247–252) and the `DetailedMinSize` constant.
   - Delete the `WidgetLayoutMode` branch in `Config_PropertyChanged` (lines 420–425).
5. **`Styles/WidgetStyles.xaml`** — delete the `DetailedMetricTile` template (lines 9–29).
   Keep the file for its shared converter declarations (`QuotaToColorConverter`,
   `ResetTimeConverter`); remove the `xmlns:controls` import (line 3).
6. **`Controls/CircularProgress.xaml` + `.xaml.cs`** — delete both files.
7. **`ViewModels/WidgetViewModel.cs`** — delete `ToggleLayout()` (lines 71–77) and its
   `[RelayCommand]`.
8. **`Views/SettingsWindow.xaml`** — drop the "Widget Format" radio group (lines 68–75).
   Its `EnumToBooleanConverter` becomes unused until Section 4 reinstates the radios — keep it.
9. **`tests/AiMeter.Tests`** — no-op: verified no test references `WidgetLayoutMode` or
   `ToggleLayout` (grepped; `SettingsViewModelResyncTests` has no layout assertions).

### Settings migration (corrected)

The enum is serialized as a **number**, not a string — `AppConfig` has no
`JsonStringEnumConverter`, so a v1.0.1 `settings.json` contains `"WidgetLayoutMode": 0`
(Detailed) or `1` (Compact) (confirmed against a live settings file).

Consequences:

- **Section 2 (property deleted):** no failure at all — System.Text.Json silently ignores
  unknown properties. No "forgiving load" work is needed; `SettingsManager.Load()` is fine
  as-is.
- **Section 4 (property reintroduced) — the real trap:** with `Compact=0, Taskbar=1`, a
  stale `"WidgetLayoutMode": 1` (old Compact) silently deserializes as **Taskbar**, booting
  existing Compact users into the new mode unasked. Numbers deserialize into any enum without
  error, so nothing catches this.

**Fix:** reintroduce the property under a new JSON name — either rename the C# property
(e.g. `LayoutMode`) or keep the name and add `[JsonPropertyName("LayoutMode")]`. The stale
`WidgetLayoutMode` field is then ignored and every upgrading user lands on the default
`Compact`. Optionally add `JsonStringEnumConverter` on the new property so future values
serialize as strings.

### What stays

Compact mode's bar template, the hover controls overlay (Refresh/Settings/Hide — minus the
layout button), the context menu (minus Switch Layout), opacity/self-heal/topmost/windowing.

---

## Section 3 — Empty-state "Log in required"

A single cross-cutting empty state that replaces the metrics list when the user has no usable
account. Three states, mutually exclusive:

| State | Condition | Shown |
|-------|-----------|-------|
| Loading | `!HasFetchedOnce` | Compact skeleton — single greyed 6px bar at placeholder width |
| Empty / log in | `HasFetchedOnce` && no provider logged in | Centered lock icon + "Log in required"; click → Settings → Accounts |
| Metrics | At least one logged-in provider with selected metrics | Normal compact bars |

### "No provider logged in" signal

`AppConfig` already tracks `HasClaudeSession` and `HasOpenCodeSession` (`AppConfig.cs:33,36`),
updated by the sessions on login/logout. The widget VM derives:

```
IsEmptyState = HasFetchedOnce && !HasClaudeSession && !HasOpenCodeSession
```

A provider that's logged in but returns only error metrics is **not** the empty state — that's
a fetch error, keep showing whatever the provider returned (today an "Error Fetching" tile).
Only "no account at all" triggers the log-in surface.

### Layout

- **Compact mode:** A slim centered row — small lock glyph + "Log in required" — sized like
  one compact bar row so the widget keeps its strip shape.
- **Taskbar mode** (Section 4): Shrinks to just the lock glyph at taskbar height; no text.
  Click → Settings.

### Click handling

The empty-state `Border` has a `MouseLeftButtonDown` handler that calls the existing
`OpenSettingsCommand` and sets `e.Handled = true` — required, because the window-level
`MouseLeftButtonDown` handler (`Window_MouseLeftButtonDown`) starts a `DragMove` and would
otherwise still fire on the same click. A click is a click, not a drag. Dragging still works
via the surrounding border margin. The hover-controls overlay (Refresh/Settings/Hide) stays
on top as today; the empty-state click coexists with the gear.

### Loading state

Replace the deleted Detailed skeleton with a Compact-shaped skeleton — a single greyed 6px bar
at placeholder width. Subtle, one row. Swaps to either the empty state or real metrics after
the first poll. No shimmer animation; static grey placeholder.

### Settings deep-link

**Correction:** SettingsWindow has no tabs — it's a single page; "Accounts" is a section
header in the right column (`SettingsWindow.xaml:98,133`). There is nothing to "land on";
`ShowOrActivate()` already surfaces the whole page including Accounts. Skip the deep-link
overload; if a stronger cue is wanted later, a brief highlight/scroll of the Accounts section
is the follow-up, not a tab switch.

### Files

- `ViewModels/WidgetViewModel.cs` — add `IsEmptyState`; reuse `OpenSettingsCommand`.
  Note: `IsEmptyState` must raise change notification when `Config.HasClaudeSession` /
  `Config.HasOpenCodeSession` change (hook `Config.PropertyChanged`) and when
  `HasFetchedOnce` flips — none of these are `[ObservableProperty]` dependencies of the VM.
- `Views/WidgetWindow.xaml` — new empty-state + loading-skeleton templates, wired via
  `Visibility` bindings.
- `Views/SettingsWindow.xaml.cs` — unchanged (deep-link dropped, see above).
- `AppConfig`/sessions unchanged — the signals already exist.

---

## Section 4 — Taskbar mode + bottom docking

The centerpiece. `WidgetLayoutMode` is reintroduced with two values: `Compact`, `Taskbar`.
The `ToggleLayoutCommand` and its `⇅` button / "Switch Layout" menu item come back, now
cycling Compact ↔ Taskbar.

### 4a. Data model — `WindowDuration`

`UsageMetric` gains a nullable `TimeSpan? WindowDuration` field, populated per metric kind:

- Claude: `session` → 5h, `weekly_all`/`weekly_scoped` → 7d.
- OpenCode: `rollingUsage` → 5h, `weeklyUsage` → 7d, `monthlyUsage` → 30d.
- Unknown/error/auth-required placeholders: `null` (the time bar hides).

`ResetTime` stays as the absolute deadline; the time-bar fraction is
`elapsed / WindowDuration = (now - (ResetTime - WindowDuration)) / WindowDuration`, clamped
0→1. When `WindowDuration` is null, the time bar collapses. Pure additive field — no
migration concern.

**Required:** `UsageMetric.UpdateFrom()` must copy `WindowDuration` too —
`ProviderManager.SyncMetrics` updates existing instances in place via `UpdateFrom`, so a
missed copy means the field is never set on any poll after a metric's first insert.

### 4b. Column template

One column per metric in a horizontal `StackPanel`. Each column, top-to-bottom inside the
fixed taskbar height (~40–48px):

```
┌──────┐
│ CSE  │   3-letter code (8pt, centered)
│█     │   vertical fill bar (quota) — fills bottom→top, colored by QuotaToColorConverter
│█▏    │   1–2px time-bar sliver hugging the quota bar's right edge (fills as reset approaches)
└──────┘
```

- **Width:** ~28–32px per column (tunable). Window grows horizontally via `SizeToContent="Width"`.
- **Height:** Fixed to detected taskbar height (see 4d). `SizeToContent="Height"` is off in
  Taskbar mode; the window has an explicit fixed height.
- **Percent + reset time:** On hover only, via a richer tooltip ("Claude Session — 74%,
  resets 5h"). Nothing else visible at rest.
- **Time bar:** A 2px-wide `Rectangle` just inside the quota bar's right edge,
  height = `timeFraction × barHeight`, `VerticalAlignment="Bottom"`, muted color
  (`#55FFFFFF`) to read as "time progress" distinct from the quota state color. Hidden when
  `WindowDuration` is null. **Encoding:** fills as reset approaches (empty = just reset,
  full = about to reset).

### 4c. 3-letter codes

A shared `MetricCodes` helper or `MetricLabelConverter` the template binds through.
**Correction:** exact-name lookup won't work — several names are dynamic. Scoped weeklies
render as `"Claude Weekly (Opus)"` / `"Claude Weekly (Scoped)"` (model name embedded,
`ClaudeWebProvider.NameForLimit`), unknown Claude kinds fall back to `"Claude {kind}"`, and
placeholder tiles have names like `"OpenCode (Auth Required)"`, `"Session Expired"`,
`"Error Fetching"`. Match by **prefix/pattern**, longest-prefix-first:

| Name pattern | Code |
|--------------|------|
| `Claude Session` | CSE |
| `Claude Weekly (…)` (any scoped variant) | CWS |
| `Claude Weekly` | CWK |
| `OpenCode Rolling` | OCR |
| `OpenCode Weekly` | OCW |
| `OpenCode Monthly` | OCM |
| Error/auth placeholders (`… Required`, `… Expired`, `… Error …`) | `!` glyph or `ERR` |

Future providers (OpenAI, Gemini, Grok) get their own prefixes (OAI, GEM, GRK). Unknown names
fall back to a 3-letter truncation of the name.

### 4d. Window behavior — docking

Taskbar mode changes `WidgetWindow`'s behavior alongside its template:

- **Fixed height:** Detect taskbar thickness as `MonitorBounds.Bottom - WorkArea.Bottom`
  (monitor bottom minus work-area bottom — corrected sign) **for the widget's monitor**.
  `SystemParameters.WorkArea` is primary-monitor-only, so it can't be used here on a
  secondary display: extend `GetMonitorBoundsForWindow` to also return `rcWork` from the
  `MONITORINFO` it already fetches (one struct read, both rects). Widget height = that value
  (typically 40–48px) minus a small margin so it sits *above* the taskbar, not overlapping.
  **Simple version: dock to the bottom edge of the work area regardless of taskbar edge
  (top/left/right); default to 40px if edge detection is fiddly.** (Recommended default;
  robust edge detection is a follow-up.)
  - **Height budget caveat:** `RootBorder` carries `Margin="6"` (12px total, reserved for
    the drop shadow) plus configurable padding — inside a ~40px window that leaves ~28px of
    content, and the hover-controls pill (~24px tall) would cover most of it. Taskbar mode
    needs slimmer chrome: reduce/remove the shadow margin and shrink or reposition the
    hover overlay in this mode. Decide during template work.
- **Dock to bottom of work area:** `Top = WorkArea.Bottom - WidgetHeight` (per-monitor work
  area, as above). `Left` is user-draggable horizontally (persist `WidgetLeft` only in
  Taskbar mode; ignore `WidgetTop` and recompute it on every layout/size/startup change).
  **Vertical dragging is disabled entirely** — it's a dock, not a free-floating window.
  Note `DragMove()` moves both axes and can't be constrained mid-drag: after the drag ends,
  the Taskbar-mode handler must actively **re-force `Top` back to the dock position** (not
  merely skip clamping it) before persisting `Left`.
- **Re-anchor on:** `OnLoaded`, `SizeChanged`, `WidgetLayoutMode` change, and a
  `SystemParameters.StaticPropertyChanged` hook for `WorkArea` (covers resolution/DPI/
  taskbar-height changes on the current monitor). Each recompute re-clamps `Left` into the
  monitor's horizontal bounds.
- **`KeepOnScreen`:** In Taskbar mode, clamp `Left` (horizontal) and **reset `Top` to the
  dock position** (see drag note above — "never touch Top" isn't enough once a `DragMove`
  has moved it). Compact mode unchanged.
- **Topmost/self-heal:** Unchanged — Taskbar mode is still an always-on-top tool window;
  the existing hook layer applies.

### 4e. Mode switch mechanics

`WidgetLayoutMode` change (via toggle button or Settings radio) drives:

- Template swap — `EnumToVisibilityConverter` bindings return: one `ItemsControl` for
  Compact, one for Taskbar, each `Visibility`-gated.
- `ApplyModeBehavior()` (replaces the old `ApplyMinSizeForLayout`): sets `SizeToContent`
  (Compact: `WidthAndHeight`; Taskbar: `Width` only), fixed `Height` in Taskbar, then
  re-anchors.

### 4f. Empty state in Taskbar mode

Section 3's empty state has a Taskbar-mode variant: just the lock glyph, centered in the
fixed taskbar height, click → Settings. No text. The loading skeleton in Taskbar mode is a
single greyed 2px-wide column placeholder.

### Files

- `Models/WidgetLayoutMode.cs` — reintroduce with `Compact`, `Taskbar`.
- `Models/AppConfig.cs` — reintroduce the layout property (default `Compact`) **under a new
  JSON name** (see Section 2 migration note) so stale numeric values can't map old Compact
  users into Taskbar mode.
- `Models/UsageMetric.cs` — add `WindowDuration` **and copy it in `UpdateFrom()`**.
- `Providers/ClaudeWebProvider.cs` / `OpenCodeProvider.cs` — populate `WindowDuration` per kind.
- `Converters/MetricLabelConverter.cs` (new) — name → 3-letter code.
- `Converters/TimeBarFractionConverter.cs` (new) — `(ResetTime, WindowDuration, Now)` → 0–1.
- `Styles/WidgetStyles.xaml` — new `TaskbarMetricColumn` template.
- `Views/WidgetWindow.xaml` — Taskbar `ItemsControl`, empty-state + loading templates.
- `Views/WidgetWindow.xaml.cs` — `ApplyModeBehavior`, dock/re-anchor logic, `KeepOnScreen`
  branching, `SystemParameters.StaticPropertyChanged` hook; extend
  `GetMonitorBoundsForWindow` to return the per-monitor `rcWork` alongside `rcMonitor`.
- `ViewModels/WidgetViewModel.cs` — reintroduce `ToggleLayout` (Compact ↔ Taskbar).
- `Views/SettingsWindow.xaml` — layout radio (Compact / Taskbar).

---

## Section 5 — Inno Setup installer

### Decisions (gathered)

- **Tooling:** Inno Setup (Pascal-scripted, single `setup.exe`, common for Windows desktop).
- **Runtime:** Framework-dependent + prerequisite check (installer stays ~15–25 MB; on
  install, checks for .NET 9 Desktop Runtime and downloads/runs the official installer if
  missing; WebView2 is preinstalled on Win11/current Win10 — fallback check only).
- **Scope:** Per-user, no admin/UAC. Installs to `%LocalAppData%\Programs\AiMeter`.
  Consistent with the existing per-user model (HKCU Run autorun, `%AppData%\AiMeter`
  settings, per-user WebView2 profile).

### Wizard flow

```
Welcome → License (optional) → Location (default %LocalAppData%\Programs\AiMeter)
  → Additional tasks: [✓] Launch at Windows startup  [✓] Desktop shortcut
  → Start menu shortcuts → Installing → Finish (optionally launch AiMeter)
```

### Autorun consistency

The installer's "Launch at Windows startup" checkbox writes to the same `HKCU\...\Run` key
with the installed EXE path that `StartupManager` (`Services/StartupManager.cs`) reads, so
the in-app Settings toggle stays in sync (both reflect/write the same registry value).
Uninstall removes the registry value and the install directory; leaves
`%AppData%\AiMeter` (user data) intact by default, with an optional "remove settings and
logs" checkbox.

### Script

- New `installer/aimeter.iss` (Inno Setup script) at repo root.
- `[Files]` pulls from the `dotnet publish -c Release -r win-x64 --self-contained false`
  output folder.
- `[Run]` prerequisite check for .NET 9 Desktop Runtime via a `Check` function reading the
  registry / `dotnet --list-runtimes`; downloads the runtime installer if missing.
- `[Icons]` Start menu + optional Desktop shortcuts.
- `[Registry]` autorun entry (gated by the checkbox task).
- `[UninstallRun]` / `[UninstallDelete]` cleanup.
- Version: read from the **published `AiMeter.exe`** via Inno's `GetVersionNumbersString()`
  preprocessor function (`FileVersion` is set in the csproj, currently 1.0.1) — simpler and
  less brittle than parsing csproj XML from the `.iss`.

### Build/publish flow

`dotnet publish -c Release -r win-x64 --self-contained false -o publish\win-x64` then
`iscc installer\aimeter.iss` produces `installer\Output\AiMeter-setup-x64.exe`. Document in
README; can be wired into a release script later.

### Files

- `installer/aimeter.iss` (new).
- `README.md` — add installer build instructions (Section 6).

---

## Section 6 — README & docs update

- `README.md`:
  - Update the **Features** list: remove the "Detailed layout" bullet; add Taskbar mode
    (docked, vertical columns, 3-letter codes, time bar) and the empty-state/"Log in
    required" surface.
  - Replace the **Download** section's zip link with the installer link (once published);
    keep the zip as a fallback or drop it.
  - Add an **Installation** subsection: run the installer, optional autorun checkbox,
    per-user (no admin), runtime prerequisite handled automatically.
  - Add an installer build line to **Build & run**.
  - Update the **Quota color states** table (unchanged) and add a **Time bar** note
    (Taskbar mode only, fills as reset approaches).
- `docs/design.md`:
  - Replace the circular-ring "Widget Layout" section with the Compact + Taskbar
    description.
  - Update the "Customization" list (drop "grid/row/column layout" → "Compact / Taskbar").
- New `docs/taskbar.png` screenshot — needs a real capture once Taskbar mode is implemented
  (deferred to manual; same pattern as the existing `docs/image*.png`).

---

## Open inputs / follow-ups

- `docs/taskbar.png` screenshot (Section 6) — captured 2026-07-16 as `docs/taskbar-mode.png`
  (along with `docs/compact-mode.png` and a refreshed `docs/settings.png`).
- Robust taskbar-edge detection (top/left/right) — deferred; simple bottom dock ships first.
- Full installer signing — not in scope; unsigned installer shows SmartScreen warning on
  first run (acceptable for a personal project; revisit if distributing widely).
- **Self-contained ("full") installer** — the shipped installer (`installer/aimeter.iss`) is
  deliberately framework-dependent with a runtime prerequisite check/download (keeps it
  ~15–25 MB, per Section 5's decision). A future version should offer a self-contained
  variant that bundles the .NET runtime (and WebView2, if feasible) directly into the
  installer, trading a larger download (~100 MB+) for zero network dependency at install
  time and no PowerShell-download fallback path — useful for offline machines or
  environments where `aka.ms`/`go.microsoft.com` are blocked. Likely a second `[Setup]`
  section/build config in the same `.iss`, or a sibling `aimeter-full.iss`, driven by
  `dotnet publish --self-contained true -r win-x64`.
- Target version number for the installer/release tag — confirm before cutting a release.

---

## Verification log (2026-07-16)

Plan verified against the codebase; corrections folded in above. Summary:

- **Confirmed accurate:** Section 1 root-cause chain (eager DI construction of the singleton
  `SettingsWindow` via `WidgetViewModel` ctor; `EnsureHandle()` in `DarkTitleBar.Apply`);
  every Section 2 line reference; `HasClaudeSession`/`HasOpenCodeSession` signals and their
  session-service writers; provider metric kinds/names; `StartupManager`'s quoted-path HKCU
  Run entry; csproj `<Version>1.0.1</Version>`.
- **Corrected:** settings migration premise (enum stored as *number*; real risk is the
  Section 4 value collision, fixed via new JSON property name); `UpdateFrom` must copy
  `WindowDuration`; taskbar-height formula sign + per-monitor work area (`rcWork`, not
  primary-only `SystemParameters.WorkArea`); Taskbar-mode drag must re-force `Top` after
  `DragMove`; metric-code map needs prefix matching (scoped weekly names are dynamic);
  no Accounts tab exists (deep-link dropped); `WidgetWindow` needs no `DarkTitleBar` call;
  test-update step is a no-op; keep the two widget converters through Section 2; Taskbar
  height budget vs. shadow margin + hover overlay flagged; Inno version read from the
  published exe.
