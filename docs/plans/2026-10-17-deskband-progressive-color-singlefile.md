# Deskband mode, progressive bar coloring & single-exe packaging — plan

**Date:** 2026-10-17
**Status:** Approved — scope decisions confirmed 2026-10-17 (see "Decisions").

## Overview

Three workstreams:

1. **Deskband mode** — a third layout mode that embeds the widget *inside* the Windows 11
   taskbar (a real deskband) using `Deskband11Lib.Wpf`, alongside the existing Compact
   (free-floating) and Taskbar (topmost dock over the taskbar) modes.
2. **Progressive bar coloring** — a per-app setting that derives a bar's color continuously
   from how much quota is left (and, optionally, from quota left *versus time* left) instead
   of the current fixed 3-threshold palette.
3. **Single-exe packaging** — package the app as one `AiMeter.exe` with no loose dependency
   files next to it. **Verified feasible** (see Section 3 for the commands and measured
   output).

## Decisions (confirmed 2026-10-17)

| # | Decision |
|---|----------|
| D1 | **Retarget to `net10.0-windows10.0.26100` and drop Windows 10 support.** `Deskband11Lib` only ships `net10.0-windows10.0.26100` assemblies (min OS = Win11 24H2, build 26100); .NET 10 is GA and Win10 reached EOL 2025-10, so one target with no conditional-compilation split. |
| D2 | **Add Deskband as a third mode.** Keep Compact (free-floating) and Taskbar (dock-over); Deskband is the Win11 24H2+ embedded mode. |
| D3 | **Implement both** `Progressive` (color by remaining %) and `Pace` (color by remaining-quota vs remaining-time). Default stays `Threshold` (current look). |

## Sequencing

| # | Work item | Depends on | Risk |
|---|-----------|-----------|------|
| 1 | Retarget to .NET 10 / Win11 26100 + restore/build/test green | D1 | medium — touches TFM, installer, runtime prerequisite |
| 2 | Progressive bar coloring | — | low — converter + one enum + settings UI |
| 3 | Deskband mode (library + new host window + lifetime) | 1, D2 | medium/high — new hosting model, Explorer/DPI recreation |
| 4 | Single-exe publish + installer/README updates | 1–3 | low — publish properties + docs |

---

## Section 1 — Deskband mode (Windows 11 taskbar-embedded)

### 1a. Target framework switch

`Deskband11Lib.Core` / `Deskband11Lib.Wpf` (v1.4.2) contain only
`lib/net10.0-windows10.0.26100/`. A `net9` or Win10-min-version project cannot reference
them. Required changes:

- `src/AiMeter/AiMeter.csproj`: `net9.0-windows` → `net10.0-windows10.0.26100.0`
  (or `net10.0-windows` with `<SupportedOSPlatformVersion>10.0.26100.0`); add
  `<PackageReference Include="Deskband11Lib.Wpf" Version="1.4.2" />`.
- `tests/AiMeter.Tests/AiMeter.Tests.csproj`: follow to `net10.0-windows10.0.26100.0`.
- `installer/aimeter.iss`: prerequisite changes from ".NET 9 Desktop Runtime" to
  "**.NET 10 Desktop Runtime**" (`IsDotNetDesktopRuntimeInstalled` matches
  `Microsoft.WindowsDesktop.App 10.`, download URL `.../10.0/windowsdesktop-runtime-win-x64.exe`).
- `README.md`: Requirements/Architecture `.NET 9` → `.NET 10`, and add
  "Windows 11 24H2 (build 26100) or later" (Windows 10 no longer supported).
- `app.manifest`: keep existing per-monitor V2 DPI; no change needed. Optionally add the
  Windows 11 `supportedOS` GUID — cosmetic only.

### 1b. Layout mode + config

- `Models/WidgetLayoutMode.cs`: append `Deskband` (`Compact=0, Taskbar=1, Deskband=2`).
  Appending is **migration-safe** — existing numeric settings (`0`/`1`) keep their meaning;
  no `Json` rename needed (unlike the v1.2 Detailed→Compact collision).
- `AppConfig`: no new field needed beyond the enum value. Add a computed
  `IsDeskbandSupported` (runtime check `OperatingSystem.IsWindows() &&
  Environment.OSVersion.Version.Build >= 26100`) exposed via `SettingsViewModel`, so
  Settings can disable the radio with a hint on unsupported machines.
- `WidgetViewModel.ToggleLayout`: cycle `Compact → Taskbar → Deskband → Compact`; skip
  Deskband when unsupported.
- `WidgetWindow`: when mode is `Deskband`, the floating window must hide and all its
  topmost/dock/self-heal behavior must stand down (it is not `Taskbar`). Its
  `EnumToVisibilityConverter` triggers need no new template — the deskband content lives in
  its own window (1c).

### 1c. New hosted window

A **separate** window is required — reusing `WidgetWindow` would drag in its console/overlay
architecture (`Topmost`, `SetWinEventHook` foreground re-assert, `WndProc` z-order hook,
`DragMove`, click-through Game mode, self-heal timer) that is meaningless or harmful for a
window that becomes a **child** of the taskbar via `SetParent`.

- `Views/DeskbandWidgetWindow.xaml` / `.xaml.cs` (new):
  - XAML: `WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"`,
    `ShowInTaskbar="False"`, **no** `Topmost`, `SizeToContent="Width"`, fixed height from the
    host options. Content = the existing `TaskbarMetricColumn` template (reuse from
    `Styles/WidgetStyles.xaml`) plus the loading/empty states; no hover controls pill (too
    small at taskbar height) — context menu (Refresh / Settings / Switch Layout) is the
    interaction surface.
  - Code-behind: construct `TaskbarContentHost(this, (FrameworkElement)Content, options)`
    and expose `Task PrepareAsync() => host.AttachWhenLayoutReadyAsync();` then `Show()`,
    mirroring the library's WPF sample.
  - Subscribes `TaskbarWindowRecreationRequired` → recreate/reattach (Explorer restart, DPI
    change, monitor disconnect).
  - `Dispose()` the host on close.
- Ownership/lifetime in `App.xaml.cs` (or a small `DeskbandHostManager`):
  - Not a DI singleton like `WidgetWindow` (it must be disposable/recreatable). Keep a
    nullable field + `CreateDeskbandWindowAsync()` / `DestroyDeskbandWindow()`.
  - On mode switch **to** Deskband: hide `WidgetWindow` (`HideByUser` semantics so self-heal
    doesn't resurrect it), create/attach the deskband window with a ~1s retry (the library's
    own sample waits for the taskbar after recreation). On switch **away**: destroy the host,
    `Show()` the floating window again.
  - Tray "Show widget" / "Hide widget" route to whichever window is active for the mode.
  - The existing `IsUserHidden` self-heal only applies to `WidgetWindow`; the deskband
    window is governed by the library's layout/visibility, so no self-heal timer.

### 1d. Host options

`TaskbarContentHostOptions` values (library-controlled placement — the user can no longer
drag the deskband horizontally):

| Option | Value |
|--------|-------|
| `PreferredWidth` | `metricColumns * 32 + padding`, recomputed when the metric collection changes (measure from the content instead if easier: `double.MaxValue` stretch mode is an alternative but would fight other slot instances). |
| `PreferredHeight` | taskbar height - small margin (reuse `GetTaskbarHeight()` math / let the library size to the taskbar). |
| `Placement` | `Auto` (spare gap around Start; falls back to before notification area). |
| `PreferredMonitorIdentity` | map the saved monitor choice to `0` (primary) / `1..n` (secondary), or default `0`. |
| `AnimateLayoutChanges` | `true`; `HighRefreshRateMode` `false`. |
| `TrackTaskbarButtons` / `TrackNotificationArea` | `true` (so it never overlaps pinned apps / tray). |
| `ManualSlotPriority` | default `65535` (arrival order). |

### 1e. Behavior that does **not** apply to deskband mode

Game-mode click-through, show-over-fullscreen, opacity-on-hover, foreground topmost
re-assert, `KeepOnScreen` docking, and drag-position persistence are all floating-window
concerns. In Deskband mode: no config writes through `PersistPosition`, `WidgetLeft/Top`
ignored, `ShowOverFullscreen`/Game mode inert (the widget is part of the taskbar).

### 1f. Files

- `src/AiMeter/AiMeter.csproj` — TFM + `Deskband11Lib.Wpf`.
- `src/AiMeter/Models/WidgetLayoutMode.cs` — add `Deskband`.
- `src/AiMeter/ViewModels/WidgetViewModel.cs` — cycle/skip logic.
- `src/AiMeter/Views/DeskbandWidgetWindow.xaml` + `.xaml.cs` — new hosted window.
- `src/AiMeter/App.xaml.cs` — create/destroy/recreate on mode switch; tray routing.
- `src/AiMeter/Views/SettingsWindow.xaml` — third radio, gated by `IsDeskbandSupported`.
- `src/AiMeter/Views/WidgetWindow.xaml(.cs)` — treat `Deskband` as "not me" (hide/stand down).
- `tests/AiMeter.Tests` — mode-cycle test (skip-when-unsupported); TFM bump.
- `installer/aimeter.iss`, `README.md` — .NET 10 + Win11 24H2 requirement.

---

## Section 2 — Progressive bar coloring

### 2a. Setting

- New `Models/BarColorMode.cs`: `Threshold` (default, current look), `Progressive`, `Pace`.
- `AppConfig.BarColorMode` (observable, default `Threshold`; `Threshold=0` keeps existing
  settings files valid).

### 2b. Converter

Replace the two `MetricColorConverter` usages (Compact fill in `WidgetWindow.xaml`, Taskbar
column fill in `Styles/WidgetStyles.xaml`) with one converter that has everything it needs:

```
MetricBarColorConverter : IMultiValueConverter
  values: [ UsageMetric metric, BarColorMode mode, DateTime now ]
```

Resolution order:
1. `metric.CustomColor` set → that color (unchanged precedence).
2. `Pace` and `metric.WindowDuration != null` and `ResetTime != null` → pace color.
3. `Progressive` → gradient color from `RemainingPercentage`.
4. else → existing `QuotaToColorConverter` thresholds.

Because the converter now consumes the metric itself (plus `Now`), the `MetricColorConverter`
can be removed and `CustomizationConverterTests` updated accordingly.

### 2c. Palettes and math

- `Progressive`: interpolate linearly in sRGB across stops, e.g.
  `0% #8B0000 → 10% #E74C3C → 25% #E67E22 → 50% #F1C40F → 75% #3498DB → 100% #2ECC71`.
  Clamp input `0..100`.
- `Pace` ("depends on time and % left"): with
  `timeElapsed = clamp((now - (ResetTime - WindowDuration)) / WindowDuration, 0, 1)` and
  `quotaUsed = 1 - RemainingPercentage/100`, `delta = quotaUsed - timeElapsed`:
  - `delta <= -0.10` → **green** (under budget, will outlast the window),
  - `-0.10 < delta < 0.10` → **blue/yellow** (on pace),
  - `delta >= 0.10` → **red** (over budget, will exhaust early).
  Interpolate across those three anchors for a continuous color; fall back to `Progressive`
  when `WindowDuration` is null (money/placeholder metrics).
- Colors are `SolidColorBrush` (frozen) newly created per call; keep it cheap (no bindings
  inside).

### 2d. Settings UI

New "COLORS" section on the General tab (or a small new tab):

```
Bar coloring
( ) Threshold — green / yellow / red (current)
( ) Progressive — smooth color by remaining %
( ) Pace — color by remaining quota vs. time to reset
```

Hint: per-metric custom colors (Metrics tab) still win over all modes.

### 2e. Files

- `src/AiMeter/Models/BarColorMode.cs` (new).
- `src/AiMeter/Models/AppConfig.cs` — `BarColorMode`.
- `src/AiMeter/Converters/MetricBarColorConverter.cs` (new) — replaces
  `MetricColorConverter.cs`.
- `src/AiMeter/Views/WidgetWindow.xaml`, `src/AiMeter/Styles/WidgetStyles.xaml` — swap
  converter bindings (3 values each).
- `src/AiMeter/Views/SettingsWindow.xaml` — radio group.
- `tests/AiMeter.Tests/CustomizationConverterTests.cs` — threshold/progressive/pace/custom.

---

## Section 3 — Single-exe packaging (research result: yes)

### 3a. Verified locally

Both publish modes were run from this repo and inspected:

```powershell
# Framework-dependent single file (needs .NET Desktop Runtime installed)
dotnet publish src/AiMeter/AiMeter.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false -o <out>
# -> AiMeter.exe  ~8.5 MB  (+ 3 Microsoft.Web.WebView2 *.xml doc files, suppressible)

# Self-contained single file (no .NET prerequisite; needs WebView2 Runtime)
dotnet publish src/AiMeter/AiMeter.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o <out>
# -> AiMeter.exe  ~65 MB
```

- The framework-dependent single exe **launched and stayed running** in a smoke test.
- `WebView2Loader.dll` (the only native dependency) was bundled and auto-extracted at first
  run to `%TEMP%\.net\AiMeter\<hash>\` — so WebView2 login still works from one file.
- `EnableCompressionInSingleFile` is **only valid for self-contained** (`NETSDK1176`
  otherwise), confirmed by the failing framework-dependent attempt.
- Remaining loose files are only XML documentation for the WebView2 packages; set
  `<PublishReferencesDocumentationFiles>false</PublishReferencesDocumentationFiles>` and
  `<SatelliteResourceLanguages>en</SatelliteResourceLanguages>` to get a clean
  single-file output. Trimming stays off (`PublishTrimmed=false`) — WPF is not
  trim-compatible.

### 3b. Recommended targets

- **Default:** framework-dependent single exe (small download; keeps the current runtime
  prerequisite model). One `AiMeter.exe` file.
- **Optional:** self-contained single exe (~65–75 MB with .NET 10 + Deskband11Lib) for
  offline / no-runtime installs — this is the "full" variant already listed as a follow-up
  in `docs/plans/2026-07-15-taskbar-mode-docking-installer-design.md`.

Caveats to document: self-contained single-file extracts to `%TEMP%` on each launch (slightly
slower first paint, temp writable required); both variants still require the WebView2
**Runtime** (preinstalled on Win11); `Assembly.Location` is unusable in single-file (AiMeter
does not use it — resources go through `pack://` URIs and `%AppData%` paths).

### 3c. csproj / installer / docs

- `src/AiMeter/AiMeter.csproj`: add a publish profile or properties so the command is one
  line, e.g. a `PublishSingleFile` conditional group and the two suppression properties
  above; optionally `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`.
- `installer/aimeter.iss`: `[Files]` still uses `recursesubdirs`, so it works unchanged with
  a single-exe publish dir; add a second `[Setup]`/define or a sibling `aimeter-full.iss`
  for the self-contained variant (per the earlier plan's follow-up).
- `README.md`: add the single-file publish commands and state that the shipped zip is now a
  single `AiMeter.exe`.

---

## Verification

- **TFM/net10:** `dotnet build -c Debug` and `dotnet test` green; app launches.
- **Deskband:** on Win11 24H2, switch to Deskband → widget appears inside the taskbar between
  the app buttons and the tray, no overlap; kill/restart `explorer.exe` → widget reappears;
  change DPI/resolution → widget reappears aligned; switch back to Compact/Taskbar → floating
  window returns, no ghost HWND.
- **Progressive:** with `Threshold` unchanged; `Progressive` produces a smooth green→red
  gradient by %; `Pace` turns red only when usage outruns the clock (verify against a known
  `ResetTime`/`WindowDuration`); custom per-metric color still overrides all three.
- **Single exe:** `dotnet publish` per 3a produces one `AiMeter.exe`; run it with no other
  files beside it; log in via WebView2 to confirm the bundled loader extracts and works;
  install via Inno from the single-exe publish dir.

## Open questions

1. Deskband `PreferredWidth`: fixed `columns × 32px`, or stretch to fill the free taskbar
   gap?
2. Should `Pace` be the default for Taskbar/Deskband layouts (where the time bar already
   exists) while Compact keeps `Threshold`?
3. Should the shipped release be the framework-dependent single exe (default) with the
   self-contained variant as an optional extra download?
