# AiMeter — implementation plan (16 items, analyzed & refined)

## Progress (updated after this session)

**Fixed this session (uncommitted):**
- **Critical startup crash**, present since Workstream D/B landed: `src/AiMeter/app.ico` was a 126-byte corrupted file (valid ICONDIR header but a bogus size field pointing past EOF). `dotnet build`/`dotnet test` never caught it because MSBuild's icon embedding is lenient, but WPF's `BitmapDecoder` isn't — `SettingsWindow.xaml`'s `Icon="pack://application:,,,/app.ico"` (Workstream B) threw `XamlParseException` during `InitializeComponent()`. Because `SettingsWindow` is now a DI singleton that `TrayViewModel`/`WidgetViewModel` construct eagerly (Workstream A1), this crashed the app on **every** launch, before any window ever appeared — an unhandled exception on the dispatcher thread that silently killed the process with no dialog. This is why the "Manual-verify still pending" list below was never actually exercised. Also found: `app.png` is mislabeled — its actual bytes are baseline JPEG (no alpha channel), not PNG.
- Regenerated `app.ico` as a proper 4-size (16/32/48/256) PNG-compressed ICO from `app.png` via `System.Drawing`. App now launches and stays running. Verified via clean rebuild (`dotnet build`/`dotnet test`, deleted `bin`/`obj` first) — still 0 errors, 16/16 tests pass — then an actual launch: widget window renders and shows "Auth Required" (matches A3), Settings/tray windows construct without crashing.
- Not yet done: visually confirm Settings window layout/titlebar icon and tray icon transparency on-screen (attempted via automated screenshot but the singleton hide/show lifecycle fights raw Win32 `ShowWindow` calls — needs a manual look, see Manual-verify list).
- **Compact widget height** (user-reported): the window's fixed `MinHeight="120"` (added in Workstream C for the Detailed skeleton) forced a ~120px window even in Compact mode, leaving a large empty area below the two bars. Moved the min-size floor out of XAML into code-behind `ApplyMinSizeForLayout()` (called from `OnLoaded` and on `WidgetLayoutMode` change): Detailed keeps the 120px floor for the skeleton, Compact uses 0 so the strip shrinks to content. Verified: Compact height 120 → **58px** (two bars, no empty space); Detailed skeleton still floors at 120 and renders fine.
- **Widget over the taskbar** (user-reported): `KeepOnScreen()` clamped to the monitor **work area** (`rcWork`, excludes the taskbar), so the widget couldn't be placed on the taskbar. Switched the clamp to full monitor **bounds** (`rcMonitor`) — renamed `GetWorkAreaForWindow` → `GetMonitorBoundsForWindow` — so the widget can sit over the taskbar (it's a taskbar-style meter) while still stopping at the physical screen edge. `AnchorToBottomRight` still uses the work area for the default first-run position. A5's on-screen clamp intent is preserved; `ScreenMath`/tests unaffected (bounds are passed in). Verified: widget renders at y 1023–1081, overlapping the taskbar, no longer snapped up to the work-area edge.
- **OpenCode Go tracking** (user question) — re-checked the public API surface as of July 2026: still **no** documented balance/usage endpoint. OpenCode issue [#10448](https://github.com/anomalyco/opencode/issues/10448) (opened Jan 2026) requests exactly a `GET /zen/v1/balance` endpoint but is open/unresolved with no PRs, and the web console's internal API is not publicly reverse-engineered. Workstream E therefore remains blocked on the **E.0 spike** (manual DevTools capture on `opencode.ai/auth`) — nothing has changed to unblock it via a documented API.

**Manual-verify completed this session (via screen automation, driving the real UI — gear icon click, radio buttons, Save & Close):**
- **A1** (singleton): clicked the widget's Settings gear 5× — only ever one `AiMeter Settings` hwnd exists. Save & Close correctly hides (not closes) it.
- **A3** (no fake metrics): fresh run with no Claude login shows only "Auth Required" in both Detailed and Compact modes — never mock metric names.
- **A5** (clamp, incidental): switching Detailed→Compact widened the widget and `KeepOnScreen()` correctly clamped its right edge to the screen boundary.
- **A6** (logging): corrupted `settings.json` with garbage text — `SettingsManager.Load()` logged a full readable `JsonException` with file path, not a silent failure.
- **B** (Settings layout): confirmed via `PrintWindow` capture — 920×520 window, 2-column layout (APPEARANCE+METRICS left, ALERTS+ACCOUNTS right) exactly as specced, titlebar shows the app icon, styled scrollbars visible, resize grip present.
- **C** (skeleton/sizing): widget opens immediately at a small fixed size (not near-invisible) rather than waiting on the first poll.
- **B** (Detailed/Compact toggle): switched via Settings radio + Save — widget re-rendered from rings to a horizontal bar layout correctly.

**Still needs an actual manual look (automation couldn't reach it in this sandbox):**
- Tray icon transparency against light/dark taskbar — this environment's system tray has an unrelated weather-widget flyout and overlay content that made automated screenshotting unreliable; the underlying `app.ico` is now confirmed as valid 32-bit RGBA at all 4 sizes, but the actual on-taskbar look wants a human glance.
- A4 (screenshot self-heal) and A5 (manual drag-past-edge) — both need a real screenshot tool / real mouse drag, not simulated `SendInput` clicks.

**Done & committed:**
- Workstream **A** (all: A1, A2, A3, A4, A5, A6) — independent bug fixes.
- Workstream **B** — Settings window rework (shared styles, 2-column layout, themed scrollbar, titlebar icon).
- Workstream **C** — `HasFetchedOnce` observable + skeleton placeholder widget.
- Workstream **D** — tray/EXE/titlebar icon fix (`app.ico` resource + `ApplicationIcon`, direct `Icon` load).
- New `tests/AiMeter.Tests` project (xUnit + FluentAssertions, hand-written fakes, no Moq), added to `AiMeter.slnx`.
- Dropped the old 9-item `docs/todo` (this plan is the complete scope).

**Deferred (blocked on inputs — do in a later session):**
- Workstream **E** (OpenCode provider) — **blocking:** the E.0 research spike must be done first. Manually log into `opencode.ai/auth` in a real browser with DevTools Network open and identify the actual JSON call the usage/balance widget makes: endpoint + response shape + session cookie name. The implementation then mirrors the existing `Claude*` pattern 1:1 (see Workstream E below for the file list). Needs to be done **after Workstream B** (uses the reworked Accounts section).
- Workstream **F** (release prep) — needs a **target version number** (currently `1.0.0.0`). Also: delete dead `MainWindow.xaml`/`MainWindow.xaml.cs`, run `dotnet build -c Release` + a trial `dotnet publish -c Release -r win-x64 --self-contained false`, add a WebView2 Runtime "Requirements" line to README.
- Workstream **G** (public readiness) — needs the **copyright holder name/handle** for the MIT LICENSE `Copyright (c) 2026 <Name>` line, and a **`docs/taskbar.png`** screenshot file (not in the repo yet). Also: fix README's dangling `## License` section, add badges row, add a keyword tagline, insert the 4th image.

**Verification (this session):**
- `dotnet test` → **16 passed**, 0 failed (covers A1 resync, A2 refilter-no-network + restore-from-cache, A6 FileLogger write/append/exception, A5 ScreenMath clamp incl. secondary monitor, C HasFetchedOnce before/after).
- `dotnet build -c Debug` and `dotnet build -c Release` → both **0 errors** (only the pre-existing `NU1701` H.NotifyIcon.Wpf warning).

**Manual-verify still pending (do by running the app — each workstream's "Verify" step):**
- A1: open Settings from tray and widget 5+× → only one window; toggle a metric, close without Save, reopen → shows saved state.
- A2: with one poll done, deselect a metric in Settings, Save → widget updates <1s; re-enable, Save → reappears from cache.
- A3: Debug run with no Claude login → only "Auth Required", never fake names.
- A4: screenshots via Win+Shift+S + Snipping Tool → widget self-heals within ~3s; context-menu Hide stays hidden.
- A5: drag widget off bottom/right edge → stops at edge; place on 2nd monitor, toggle Detailed/Compact → stays on that monitor.
- A6: disconnect network or corrupt `settings.json` → `%AppData%\AiMeter\logs\aimeter-{yyyy-MM-dd}.log` has a readable error entry.
- B: Settings window is 16:9-ish, both columns render at min size, custom scrollbar renders when forced to scroll, titlebar shows AiMeter icon; Detailed/Compact ring layout still renders after tile extraction.
- C: run with clean `%AppData%\AiMeter\settings.json` → widget opens at a fixed sensible size with skeleton tile, swaps to real content after first poll.
- D: tray icon transparent on light + dark taskbars; EXE's Explorer icon + Settings titlebar icon both correct.

**Key new/changed files (for the next session's reference):**
- `tests/AiMeter.Tests/` — `ProviderManagerRefilterTests.cs`, `SettingsViewModelResyncTests.cs`, `FileLoggerTests.cs`, `ScreenMathTests.cs`, `ProviderManagerHasFetchedOnceTests.cs`, `InfrastructureTests.cs`, `Fakes/` (`FakeProvider`, `FakeSettingsManager`, `FakeClaudeSession`, `FakeServiceProvider`).
- `src/AiMeter/Logging/` — `FileLogger.cs`, `FileLoggerProvider.cs`, `LoggingExtensions.cs`.
- `src/AiMeter/Windowing/ScreenMath.cs` — pure clamp math (unit-tested).
- `src/AiMeter/Styles/` — `SettingsStyles.xaml`, `WidgetStyles.xaml`.
- `src/AiMeter/Converters/InverseBooleanToVisibilityConverter.cs`.

---

## Context

AiMeter is a .NET 9 WPF desktop app (`src/AiMeter`) that shows Claude.ai usage quotas in an always-on-top floating widget, with a tray icon and a Settings window. It has accumulated a backlog spanning real bugs (settings window duplicating, widget disappearing after screenshots), missing polish (scrollbar styling, window proportions, icons), an unfinished feature seam (a second usage provider beyond Claude), and pre-release housekeeping (logging, LICENSE, README). This plan bundles all 16 items into ordered workstreams so related files are touched once instead of repeatedly.

Every root-cause claim below was verified directly against the codebase (`App.xaml.cs`, `ProviderManager.cs`, `WidgetWindow.xaml.cs`, `SettingsViewModel.cs`, `SettingsWindow.xaml`, `AiMeter.csproj`, `Managers/SettingsManager.cs`).

**Scope decisions (confirmed):**
- Item 1 ("refactor detailed styled UI") covers **both** the Settings window and the widget's Detailed/rings mode.
- Item 8/2 (OpenCode provider): no documented usage/balance API exists publicly — implementation mirrors the existing Claude cookie-session pattern, but needs a manual research spike first (see Workstream E).
- Item 13 (release): **prepare only** — no git tag, no GitHub release, no publish.
- Item 14 (public readiness): add an MIT LICENSE and fix README's dead license reference; **no** GitHub repo visibility change — that stays a manual step.
- The old 9-item `docs/todo` list is **dropped** — this plan is the complete scope.

---

## Workstream A — Independent bug fixes (do first)

### A1. Settings window opens multiple times (item 5)
Root cause: `SettingsWindow`/`SettingsViewModel` are `AddTransient` in `App.xaml.cs` (lines 42, 46), and both `TrayViewModel.ShowSettings()` and `WidgetViewModel.OpenSettings()` blindly `GetRequiredService<SettingsWindow>().Show()` with no existence check — every call builds a brand-new window + viewmodel. `SettingsViewModel`'s constructor also subscribes to `ProviderManager.KnownMetricNames.CollectionChanged` (`SettingsViewModel.cs:44`) and never unsubscribes, so each duplicate open leaks another subscription on the singleton `ProviderManager`.

- `App.xaml.cs`: change `SettingsWindow` and `SettingsViewModel` registrations from `AddTransient` to `AddSingleton`.
- `Views/SettingsWindow.xaml.cs`: handle `Closing` with `e.Cancel = true; Hide();` (a singleton window must never actually `Close()`, which disposes it permanently). Add a `ShowOrActivate()` method (`Show()` + un-minimize + `Activate()`, mirroring `TrayViewModel.ShowWidget()`'s existing pattern).
- `ViewModels/SettingsViewModel.cs` `Save()`: change `window?.Close()` → `window?.Hide()`.
- `ViewModels/TrayViewModel.cs` / `ViewModels/WidgetViewModel.cs`: inject `SettingsWindow` via constructor instead of resolving through `IServiceProvider` per call; call `settingsWindow.ShowOrActivate()`.
- The `CollectionChanged` leak disappears as a side effect — the VM now constructs exactly once for the app's lifetime.
- **Resync VM state on reopen:** a singleton VM with hide-on-close changes cancel semantics — unsaved `MetricOptions.IsSelected` edits would silently survive a close-without-Save and reappear next open. In `ShowOrActivate()` (or `IsVisibleChanged`), resync each `MetricOptions[i].IsSelected` from `Config.SelectedMetrics` so reopening always reflects saved state.

**Verify:** run the app, open Settings from the tray icon and from the widget context menu 5+ times — only one window ever exists, Save/close hides it. Toggle a metric checkbox, close without Save, reopen — the checkbox shows the saved state, not the abandoned edit.

### A2. Widget doesn't update immediately after Settings save (item 10)
`ProviderManager.RefreshAsync()` (`Managers/ProviderManager.cs:55-87`) already separates fetch (`_lastGoodByProvider`) from filter (`all`/`latest`/`SyncMetrics`) — there's just no entry point to re-run only the filter step, so a new metric selection only takes effect on the next poll tick (default 60s) or a manual "Refresh Now."

- `Managers/ProviderManager.cs`: extract the filtering tail (lines 70-86) into a reusable path and add a public `RefilterMetrics()` that re-filters the already-cached `_lastGoodByProvider` data with **no network call**, calling `SyncMetrics` directly (skip `DetectAlerts` — alerts should only fire on genuinely new readings, not a settings-triggered re-filter).
- `Managers/IProviderManager.cs`: add `void RefilterMetrics();`.
- `ViewModels/SettingsViewModel.cs` `Save()`: after `_settingsManager.Save()`, call `_providerManager.RefilterMetrics();`.

This updates the widget instantly with no extra network call, and instantly restores a metric the user just re-enabled since `_lastGoodByProvider` retains everything ever fetched.

**Verify:** let one poll complete so multiple metrics show, deselect one in Settings, Save — widget updates in well under a second. Re-enable it, Save — it reappears immediately with its last-known value.

### A3. Remove fake metrics (item 9)
`Providers/MockProvider.cs` hardcodes "Claude Messages", "Claude Max", "GPT-4" with random values, registered only `#if DEBUG` in `App.xaml.cs` (lines 33-37) — which is why they show up in a Debug run.

- Delete `Providers/MockProvider.cs`.
- `App.xaml.cs`: remove the `#if DEBUG ... AddTransient<IProvider, MockProvider>() ... #endif` block.

**Verify:** Debug build, run without logging into Claude — only "Auth Required" placeholder should appear, never the fake names.

### A4. Widget disappears after a screenshot (item 4)
`WidgetWindow.xaml.cs` installs a global `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ...)` that calls `ReassertTopmost()` on every foreground-window change — but `ReassertTopmost()` early-returns when `!IsVisible` (`WidgetWindow.xaml.cs:140`), so the existing hook can *never* recover a window something external has hidden. No code path calls `.Hide()` in response to focus loss (the only `Hide()` is the user's explicit command). The exact OS-level race with the screenshot tool's own topmost overlay isn't 100% pinned down, so the fix is a **self-heal timer** layered on top of the existing event hook rather than chasing one exact cause:

- `Views/WidgetWindow.xaml.cs`: add a `DispatcherTimer` (~2-3s interval) that unconditionally re-asserts topmost and, if `!IsVisible` and not user-hidden, calls `Show()` to auto-recover.
- Add an `IsUserHidden` flag and a `HideByUser()` method so the self-heal timer can tell "user deliberately hid it" apart from "something external made it disappear." `WidgetViewModel`'s Hide command calls `HideByUser()` instead of `Hide()` directly.
- `TrayViewModel.ShowWidget()`: clear `IsUserHidden` before showing.

**Verify:** take screenshots with Win+Shift+S (all modes) and the classic Snipping Tool — widget stays visible/topmost within ~3s with no manual interaction. Confirm the context-menu "Hide" still works and stays hidden (self-heal must not resurrect an intentional hide).

### A5. Widget can be dragged past the bottom of the screen (item 12)
`KeepOnScreen()` (`WidgetWindow.xaml.cs:126-136`) already clamps position to `SystemParameters.WorkArea`, but it's only called from `SizeChanged` and once at startup — never after `DragMove()` returns.

- Call `KeepOnScreen()` immediately after `DragMove()` returns, before `PersistPosition`.
- Extend clamping to the monitor the widget is actually on (not just the primary display) via `System.Windows.Forms.Screen.FromHandle`, converting to WPF units via the window's DPI scale. **This is a required bug fix, not optional polish:** because `SizeChanged` already calls `KeepOnScreen()`, a widget placed on a secondary monitor gets snapped back to the primary monitor on *any* size change (metric count change, layout toggle) today. Fixing the clamp inside `KeepOnScreen()` fixes all call sites at once. Mixed-DPI multi-monitor is a known follow-up if it's ever actually hit.

**Verify:** drag the widget so its bottom/right edge would exceed the screen edge — it stops exactly at the edge. Place the widget on a secondary monitor and toggle Detailed/Compact — it must stay on that monitor.

### A6. Add logging for silent failures (item 15)
There is no logging today beyond two `Console.WriteLine` calls in `Managers/SettingsManager.cs` (lines 49, 63 — invisible in a normal WPF launch). Every provider/network failure is caught and silently swallowed into placeholder metrics — this is exactly why "no metrics, no idea why" is currently undiagnosable. `Microsoft.Extensions.Hosting` is already wired up, so `Microsoft.Extensions.Logging` needs no new package.

- New `Logging/FileLogger.cs` + `FileLoggerProvider.cs` — minimal provider writing to `%AppData%\AiMeter\logs\aimeter-{yyyy-MM-dd}.log`.
- `App.xaml.cs`: register the file provider via `.ConfigureLogging(...)`.
- Inject `ILogger<T>` and log at each currently-silent catch/placeholder branch in `ClaudeWebProvider.cs`, `ProviderManager.RefreshAsync`, `Managers/SettingsManager.cs`, `ClaudeApiClient.cs`, `ClaudeSession.cs`.
- `README.md`: note the log file location.

**Verify:** force a failure (disconnect network, or corrupt `settings.json`) and confirm the log file contains a readable error entry.

---

## Workstream B — Settings window rework (items 1-settings-half, 6, 7, 11-titlebar-half)

Do after A1, since A1 changes `SettingsWindow.xaml.cs`'s close/show behavior — land behavior fixes before a visual rewrite touches the same file.

- New `Styles/SettingsStyles.xaml`: extract `SectionHeader`/`HintText`/`SectionCard`/checkbox/radio styles out of `SettingsWindow.xaml`'s inline `<Window.Resources>` (lines 14-43) into a shared, mergeable dictionary. Add an implicit `Style TargetType="ScrollBar"` here too (item 6) — a themed Track/Thumb/RepeatButton template matching the app's dark palette — applied automatically to any `ScrollViewer` in the window.
- `Views/SettingsWindow.xaml`:
  - `Icon="pack://application:,,,/app.ico"` on the `Window` (item 11, titlebar icon).
  - Resize from the current portrait `500x620` to a 16:9-ish `Width=920 Height=520` (`MinWidth=760 MinHeight=440`) — item 7.
  - Restructure the single vertical stack of 4 section cards into a 2-column grid (left: APPEARANCE + METRICS SHOWN; right: ALERTS + ACCOUNTS), title and Save rows spanning both columns.
- `Views/WidgetWindow.xaml`: extract the Detailed-mode per-metric tile into a named resource in a new `Styles/WidgetStyles.xaml`, polish spacing/typography (item 1, widget half). Exact spacing is refined interactively while running the app rather than fully speced upfront.

**Verify:** open Settings — confirm 16:9 layout, both columns render without clipping at min size, the custom scrollbar renders when content is forced to scroll, and the titlebar shows the AiMeter icon. Toggle the widget between Detailed/Compact to confirm the ring layout still renders correctly post-extraction.

---

## Workstream C — Widget fixed-size skeleton state (item 16)

No hard dependency on B, but pairs naturally with its widget visual pass.

- `ProviderManager`: add a `HasFetchedOnce` observable property, set `true` at the end of the first `RefreshAsync()`.
- `WidgetViewModel`: expose `HasFetchedOnce` for binding.
- New `Converters/InverseBooleanToVisibilityConverter.cs` (matches the existing converter style).
- `Views/WidgetWindow.xaml`: add `MinWidth`/`MinHeight` sized to fit one Detailed-mode tile plus titlebar, so the window (which still grows via `SizeToContent` beyond this) never starts near-invisible. Show a greyed-out skeleton tile at that same size while `!HasFetchedOnce`, swapped for real content once the first poll completes.

**Verify:** run with a clean `%AppData%\AiMeter\settings.json` (simulate first run) — widget appears immediately at a fixed, sensible size with a skeleton placeholder, then swaps to real content and grows further if more metrics arrive.

---

## Workstream D — Tray/window icon fixes (item 11, tray half)

Independent; run any time before Workstream F's build sanity check.

`App.xaml.cs` builds the tray icon at runtime via `new Bitmap(app.png).GetHicon()` → `Icon.FromHandle` (lines 68-73) — `GetHicon()` doesn't preserve alpha, producing an opaque background even though the source PNG is transparent. A proper `app.ico` already exists in the project but is completely unreferenced (csproj only includes `app.png`).

- `AiMeter.csproj`: add `<Resource Include="app.ico" />` and `<ApplicationIcon>app.ico</ApplicationIcon>` (fixes EXE/taskbar/alt-tab icon too).
- `App.xaml.cs`: load `app.ico` directly via `new System.Drawing.Icon(iconStream)` instead of the `GetHicon()` round-trip.

**Verify:** inspect the tray icon against light and dark taskbars (background should be transparent), and check the EXE's Explorer icon and the Settings titlebar icon (from Workstream B) are both correct.

---

## Workstream E — New provider: OpenCode (items 2 + 8, merged)

> **STATUS (this session): DONE — implemented, reverse-engineered, and verified live.**
> The OpenCode provider is complete and working end-to-end: logged into opencode.ai via the app's new
> "Log into OpenCode" button, and the widget shows **OpenCode Rolling / Weekly / Monthly** with correct
> live values (e.g. Rolling 96% remaining, resets 3h; Weekly 73%; Monthly 87%). Build 0 errors, **20/20
> tests pass** (4 new `OpenCodeProviderTests` covering the two-step fetch + parse against the real payload
> shape, incl. the `monthlyUsage:null` decoy).
>
> **How OpenCode Go usage actually works (reverse-engineered from a live session — supersedes the E.0
> guesses below):** there is no JSON API. It's a SolidStart SSR app, two steps like Claude's org→usage:
>   1. `GET /go` (authenticated) renders the user's workspace CTA `<a href="/workspace/{id}/go">` — scrape
>      that path (so the workspace id is discovered, not hardcoded). `/go` alone is the public marketing page.
>   2. `GET /workspace/{id}/go` embeds the stats in its SolidStart hydration payload as
>      `rollingUsage:$R[n]={status:"ok",resetInSec:13824,usagePercent:4}` (+ `weeklyUsage`, `monthlyUsage`).
>      `usagePercent` = percent used → RemainingQuota = 100 − used; `resetInSec` → ResetTime = now + seconds.
> `OpenCodeProvider.ParseUsage` regexes those three objects (the `$R[n]=` seroval tag is optional; requiring
> the `{` skips the unrelated `monthlyUsage:null` in the billing object).
>
> **Files added:** `Services/IProviderSession.cs`, `IOpenCodeSession.cs`, `OpenCodeSession.cs`,
> `IOpenCodeApiClient.cs`, `OpenCodeApiClient.cs`, `Providers/OpenCodeProvider.cs`,
> `Views/OpenCodeAuthWindow.xaml(.cs)`, `ViewModels/AccountRowViewModel.cs`, tests `OpenCodeProviderTests.cs`,
> `Fakes/FakeOpenCodeSession.cs`, `Fakes/FakeOpenCodeApiClient.cs`. **Changed:** `AppConfig`
> (`HasOpenCodeSession`), `IClaudeSession`/`ClaudeSession` (now implement `IProviderSession` + `ProviderName`),
> `SettingsViewModel` (generalized `Accounts` collection), `SettingsWindow.xaml` (ACCOUNTS → `ItemsControl`),
> `App.xaml.cs` (DI + dispose). The provider makes no network call / no WebView2 until login, so it's safe.
> The three OpenCode metrics are discovered but not auto-selected (respecting an existing metric selection) —
> enable them in Settings ▸ METRICS SHOWN.
>
> **Possible follow-ups:** multi-workspace users get the first `/workspace/.../go` link only; `OpenCodeSession.Store`
> still marks the session on "any cookie present" (works, but could be tightened to the real cookie name).

**E.0 — research spike (superseded by the STATUS block above — kept for history):** No documented usage/balance API exists for opencode.ai. OpenCode Zen's documented endpoints (`/zen/v1/models`, `/responses`, `/messages`) are only for making model calls; OpenCode Go's usage is described as visible only in the web console at `opencode.ai/auth`. This means the same approach as Claude is needed: manually log into `opencode.ai/auth` in a real browser with DevTools Network tab open, and identify the actual JSON call the usage/balance widget makes (endpoint + response shape + session cookie name) — mirroring how `ClaudeWebProvider`'s `/api/organizations/{id}/usage` was originally reverse-engineered in this codebase. The local `opencode` CLI's `~/.local/share/opencode/auth.json` is a different surface (CLI provider credentials) and isn't useful here.

Once the spike identifies the real endpoint, implementation mirrors the existing `Claude*` pattern 1:1:
- `Models/AppConfig.cs`: add `HasOpenCodeSession`.
- `Services/IOpenCodeSession.cs`/`OpenCodeSession.cs` — mirrors `IClaudeSession`/`ClaudeSession.cs`.
- `Services/IOpenCodeApiClient.cs`/`OpenCodeApiClient.cs` — mirrors `IClaudeApiClient`/`ClaudeApiClient.cs`; hidden WebView2 navigated to `opencode.ai`, same postMessage/fetch bridge.
- `Providers/OpenCodeProvider.cs implements IProvider` — `Name => "OpenCode"`, "Auth Required" placeholder when logged out, maps discovered fields to `UsageMetric`s once logged in.
- `Views/OpenCodeAuthWindow.xaml`/`.xaml.cs` — mirrors `AuthWindow.xaml.cs`, navigates to `opencode.ai/auth`, watches for the session cookie found in E.0.
- `App.xaml.cs`: register the new session/client/provider/auth-window, dispose the API client in `OnExit` alongside the existing Claude one.
- Settings UI: generalize the ACCOUNTS section (`SettingsViewModel.cs`) from single hardcoded Claude login button/status into an `ObservableCollection` of per-provider account rows, so adding OpenCode's row doesn't duplicate the Claude-only properties — this also pays off if more providers (OpenAI, Gemini, etc., per the README's stated roadmap) are added later.
- Do this workstream **after Workstream B**, since it needs the reworked Accounts section to add a second row into.
- Not recommended now: extracting a fully generic `BrowserSessionBase`/`BrowserApiClientBase` shared by Claude+OpenCode — with only 2 providers, duplicating the pattern is lower-risk; revisit if a 3rd provider arrives.

**Verify:** open Settings, log into OpenCode via the embedded browser, confirm status flips to "Logged In," confirm the widget shows an "OpenCode ..." metric with a plausible value, confirm logout clears the session and reverts to "Auth Required."

---

## Workstream F — Release prep (item 13) — prepare only, no publish

- `AiMeter.csproj`: add `<Version>`/`<AssemblyVersion>`/`<FileVersion>` (currently absent, defaults to `1.0.0.0`) — confirm target version number before setting it.
- Delete dead `MainWindow.xaml`/`MainWindow.xaml.cs` — referenced nowhere (no `StartupUri`, no code reference).
- Sanity build: `dotnet build -c Release` and a trial `dotnet publish -c Release -r win-x64 --self-contained false`, confirming no leftover `#if DEBUG` branches remain (A3 removes the only one currently present).
- `README.md`: add a "Requirements" line noting the WebView2 Runtime dependency (currently unmentioned, though both Claude and OpenCode providers hard-depend on it).

**Verify:** both build/publish commands exit 0 with no missing-resource warnings (also validates Workstream D's icon wiring); launch the Release build once to confirm normal startup.

---

## Workstream G — Public readiness (item 14) + README/SEO (item 3)

- New `LICENSE` file at repo root — MIT text. Needs the copyright holder name/handle to put in `Copyright (c) 2026 <Name>` — confirm before adding.
- `README.md`:
  - Replace the dangling `## License` section (currently "See repository for license details," pointing at nothing) with a proper link to the new `LICENSE` file.
  - Add a badges row (.NET 9, Windows, License: MIT — no build-status badge since there's no CI workflow).
  - Add a short keyword-rich tagline near the top (e.g. "Claude usage tracker," "AI quota widget for Windows," "system tray usage meter") for discoverability.
  - Insert the taskbar screenshot as a 4th image alongside the existing `docs/image1.png`/`image2.png`/`docs/settings.png` — **requires saving the taskbar screenshot as `docs/taskbar.png`**, since it isn't a file in the repo yet.
- No repo-visibility change — stays a manual step.

**Verify:** preview `README.md` locally to confirm badges/images resolve and the license section links correctly; repo already scanned clean of secrets/personal paths in the research pass (only gap was the missing LICENSE, fixed here).

---

## Suggested execution order

1. **Workstream A** (bug fixes) — independent, suggested A1 → A2 → A3 → A6 → A4 → A5.
2. **Workstream D** (icon fix) — trivial, do before F's build check.
3. **Workstream B** (settings rework) — after A1.
4. **Workstream C** (widget skeleton) — pairs with B, no hard dependency.
5. **Workstream E** (OpenCode provider) — after B; E.0 research spike is the literal first step and needs manual involvement (logging into opencode.ai and inspecting network traffic).
6. **Workstream F** (release prep) — after A, D, E.
7. **Workstream G** (public readiness) — independent, naturally last since it's docs + a LICENSE file.

## Open inputs needed at execution time
- Target version number (Workstream F).
- Copyright holder name for the MIT LICENSE (Workstream G).
- `docs/taskbar.png` screenshot file (Workstream G).
- E.0 spike results: OpenCode usage endpoint, response shape, session cookie name (Workstream E).

## Critical files
- `src/AiMeter/App.xaml.cs` — DI registrations, tray icon setup, startup sequencing
- `src/AiMeter/Managers/ProviderManager.cs` — fetch/filter/sync pipeline
- `src/AiMeter/Managers/SettingsManager.cs` — settings load/save, logging target (A6)
- `src/AiMeter/Views/WidgetWindow.xaml(.cs)` — window behavior, drag/clamp, foreground hook
- `src/AiMeter/Views/SettingsWindow.xaml(.cs)` — layout, styling, lifecycle
- `src/AiMeter/ViewModels/SettingsViewModel.cs` — save/filter wiring, VM state resync
- `src/AiMeter/Providers/ClaudeWebProvider.cs` — reference pattern for `OpenCodeProvider`
- `src/AiMeter/AiMeter.csproj` — icon resources, version (D/F)
