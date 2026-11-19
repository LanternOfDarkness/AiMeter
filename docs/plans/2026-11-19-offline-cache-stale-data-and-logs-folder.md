# Offline cache, stale-data warning & logs-folder button — plan

**Date:** 2026-11-19
**Status:** Implemented 2026-11-19 — commits `27d7da0`, `4ee0c37`, `d24cee8`, `835edf0`, `27b89f9`, `b2e1b3c`, `4c261af`, `ac68dbc`. Manual offline/UI verification is not done.
**Out of scope:** multiple OpenCode accounts (deferred to a later version)

## Implementation status (2026-11-19)

- **Delivered:** explicit `ProviderFetchResult` contract; persisted last-good snapshot at
  `%AppData%\AiMeter\cache.json` with startup restore; stale `⚠` + dimmed fill + age tooltip
  in Compact, Taskbar and Deskband; auth/no-data cache invalidation; OpenCode WebView2 init
  retry; Settings → General → Diagnostics → **Open logs folder**; privacy/site copy; README
  feature bullets and `CHANGELOG.md` Unreleased entries (`4c261af`).
- **Verified:** `dotnet build AiMeter.slnx` succeeded; `dotnet test
  tests/AiMeter.Tests/AiMeter.Tests.csproj` passed (106 tests, 0 failed). Two review rounds;
  the UTC/local stale-age defect and the Claude `/usage` 401/403 mapping were fixed in
  `27b89f9`.
- **Not done:** manual network-block / restart / three-layout / Explorer-open checks in a live
  WebView2 session.

## Overview

Today an outage removes the bars instead of leaving the last known values on screen. This
plan keeps the last successful metrics in memory and on disk, restores them at startup, and
marks them visibly stale until a refresh succeeds. It also adds a Settings button that opens
the log folder (`%AppData%\AiMeter\logs`).

Deliverables:

1. **Cached metrics** — last successful per-provider snapshot survives network failures and
   app restarts.
2. **Stale indicator** — per-metric warning glyph, desaturated bar fill, and a tooltip line
   showing how old the data is. Works in Compact, Taskbar and Deskband.
3. **Logs button** — Settings → General → Diagnostics → **Open logs folder**.

## Why the bars disappear today

- Both providers translate every failure into placeholder metrics:
  `ClaudeWebProvider.GetMetricsAsync` returns `"Error Fetching"` on a non-200 response or
  exception (`src/AiMeter/Providers/ClaudeWebProvider.cs:43-70`); `OpenCodeProvider` returns
  `"OpenCode (Error Fetching)"` when the page never renders or the fetch throws
  (`src/AiMeter/Providers/OpenCodeProvider.cs:92-126`).
- `ProviderManager.RefreshAsync` stores whatever the provider returned in
  `_lastGoodByProvider` (`src/AiMeter/Managers/ProviderManager.cs:67`). A failure therefore
  overwrites the last real values with a placeholder.
- `ProviderManager.SelectedMetrics` drops placeholders whenever at least one real metric
  exists (`ProviderManager.cs:103-120`), so a failed provider's bars simply vanish instead of
  showing an error.
- Nothing is persisted; an app restart starts empty until the first successful poll.
- `OpenCodeApiClient.EnsureInitializedAsync` caches the init task even when it faults
  (`src/AiMeter/Services/OpenCodeApiClient.cs:147`). If WebView2 initialization fails during
  an outage, every later poll reuses the failed task until restart.

## Decisions

| # | Decision |
|---|----------|
| D1 | Cache only **successful, real** metrics. Placeholder metrics are never written to disk. |
| D2 | Only a **transient fetch failure** (`Error`) falls back to cached data. `NoSession`, `SessionExpired` and `NoData` invalidate the cache, because showing bars for a logged-out/expired account is misleading. |
| D3 | Stale presentation: amber `⚠` glyph + bar fill at 45% opacity + tooltip `Not updated — last successful update 5m ago`. The bar keeps its quota hue, so severity stays readable. |
| D4 | Cache file: `%AppData%\AiMeter\cache.json`, versioned JSON, atomic write, parse/write failures are logged and never break polling. Contains usage numbers and timestamps only — no cookies or credentials. |
| D5 | Restored cache is displayed **immediately at startup**, marked stale, while the first poll runs. A successful poll clears the stale mark and advances the timestamp. |
| D6 | Replace implicit placeholder-name sniffing with an explicit `ProviderFetchResult`/`ProviderFetchStatus` contract returned by `IProvider`. |
| D7 | Logs button lives in Settings → General → Diagnostics. The log path is exposed by one shared helper, not duplicated between the logger and the UI. |

## Sequencing

| # | Work item | Depends on | Risk |
|---|-----------|-----------|------|
| 1 | `ProviderFetchResult` contract + update Claude/OpenCode providers + fakes | — | low/medium — touches every provider and its tests |
| 2 | `ProviderManager` last-good/cache state machine | 1 | medium — selection, alerts and refilter must keep working |
| 3 | `IMetricCacheStore` file persistence + startup restore | 2 | low — isolated I/O with failure fallback |
| 4 | Stale fields + Compact/Taskbar/Deskband UI + age converter | 2 | low/medium — three templates |
| 5 | OpenCode WebView init retry | — | low — small reliability fix |
| 6 | Logs-folder helper + Settings button | — | low |
| 7 | Docs/privacy/changelog update | 1–6 | low |

---

## Section 1 — Explicit provider result contract

### 1a. New types

`src/AiMeter/Providers/ProviderFetchResult.cs`:

```csharp
public enum ProviderFetchStatus
{
    Success,        // metrics are live and authoritative
    NoSession,      // user is not logged in
    SessionExpired, // server rejected the session; session was cleared
    NoData,         // logged in, but the plan exposes nothing to meter (e.g. no Go card)
    Error           // transient failure (network, timeout, page never rendered)
}

public sealed record ProviderFetchResult(
    IReadOnlyList<UsageMetric> Metrics,
    ProviderFetchStatus Status);
```

`IProvider.GetMetricsAsync()` returns `Task<ProviderFetchResult>`.

### 1b. Provider mappings

| Provider outcome | Status | Placeholder shown when no cache |
|---|---|---|
| Not logged in | `NoSession` | `Auth Required` / `OpenCode (Auth Required)` |
| 401/403 or bounced to login | `SessionExpired` | `Session Expired` / `OpenCode (Session Expired)` |
| Successful parse | `Success` | — |
| Logged in, page ready, no Go card | `NoData` | `OpenCode (No Subscription)` |
| HTTP failure, exception, page never ready, no workspace | `Error` | `Error Fetching` / `OpenCode (Error Fetching)` |

Keep `ProviderManager.IsPlaceholderMetric` unchanged — it still controls which metrics are
hidden from Settings/selection. Status no longer depends on its name matching.

Update the test fake `FakeProvider` (`tests/AiMeter.Tests/Fakes/FakeProvider.cs`) to return
`ProviderFetchResult` with a settable `Status`, defaulting to `Success`.

---

## Section 2 — `ProviderManager` cache state machine

### 2a. State

Replace `Dictionary<string, IReadOnlyList<UsageMetric>> _lastGoodByProvider` with:

```csharp
private sealed record ProviderState(
    IReadOnlyList<UsageMetric> Metrics,
    DateTime? LastSuccessUtc,
    bool IsStale);

private readonly Dictionary<string, ProviderState> _stateByProvider = new();
private readonly IMetricCacheStore _cache;
private readonly SemaphoreSlim _refreshLock = new(1, 1);
```

`RefilterMetrics` and `SelectedMetrics` read `_stateByProvider.Values.SelectMany(s => s.Metrics)`
instead of `_lastGoodByProvider.Values`.

### 2b. Per-provider transition

```
result = await provider.GetMetricsAsync()   // exceptions -> Error with placeholder
switch result.Status:
    Success:
        real = result.Metrics.Where(m => !IsPlaceholderMetric(m.Name))
        if real.Count == 0:
            cache.Remove(provider.Name)
            state = new(real, now, false)
        else:
            stamp every real metric: LastUpdatedUtc = now, IsStale = false
            state = new(real, now, false)
            cache.Set(provider.Name, real, now)
    Error:
        if cache has a real snapshot for provider.Name:
            stamp snapshot metrics: IsStale = true (keep original LastUpdatedUtc)
            state = new(snapshot.Metrics, snapshot.FetchedAtUtc, true)
        else:
            state = new(result.Metrics, null, false)   // current error tile behavior
    NoSession / SessionExpired / NoData:
        cache.Remove(provider.Name)
        state = new(result.Metrics, null, false)
```

Rules:

- A successful fetch replaces the whole provider snapshot, so a meter that disappeared is
  removed rather than resurrected from cache.
- Cached metrics are never mutated into a different quota value; only `IsStale` and
  `LastUpdatedUtc` metadata are set.
- On success after an outage, `IsStale` clears immediately and the timestamp advances.
- `DetectAlerts` still runs on the published list. Stale snapshots repeat the same values, so
  they cannot raise duplicate low/exhausted alerts. After a restart `_previousByMetricName` is
  empty, so restored cache cannot fire a reset alert either.
- Serialize `RefreshAsync` with `_refreshLock` so a manual refresh and the timer cannot race
  the cache dictionary or file.

### 2c. Startup restore

`ProviderManager` constructor loads the cache through `IMetricCacheStore` (or `StartAsync`
does, before starting the poll loop). If any non-empty snapshot exists:

- put it in `_stateByProvider`, mark every metric `IsStale = true`, `LastUpdatedUtc = FetchedAtUtc`;
- run the existing `TrackKnownMetricNames()` + `SyncMetrics()` so bars appear immediately;
- set `HasFetchedOnce = true` so `WidgetViewModel.ShowSkeleton`/`ShowRealContent` show the
  cached bars rather than the loading skeleton;
- do **not** raise alerts.

Then the first poll runs normally. Offline, it takes at most one provider timeout before the
bars stay on screen with the stale mark.

### 2d. Session/logout interaction

- Explicit logout: `AccountRowViewModel` already triggers `RefreshNow()`, which returns
  `NoSession` and removes the provider's cache entry. Cached bars may remain on screen for the
  duration of that one refresh, then disappear with the normal auth-required/empty state.
- Session expiry: the provider clears its session marker before returning `SessionExpired`, and
  the manager removes the cache entry. No stale bars for an expired login.
- If all providers are logged out, `WidgetViewModel.IsEmptyState` hides cached content exactly
  as it does today.

### 2e. Selection, labels, colors

No format or key changes: cache entries use the same metric names as live data
(`Claude Session`, `OpenCode Weekly`, …). Existing `SelectedMetrics`, `MetricLabels` and
`MetricColors` continue to apply. Placeholder metrics still never enter Settings.

---

## Section 3 — Disk cache

### 3a. Interface

`src/AiMeter/Services/IMetricCacheStore.cs`:

```csharp
public interface IMetricCacheStore
{
    bool TryGet(string providerName, out CachedProviderMetrics snapshot);
    void Set(string providerName, IReadOnlyList<UsageMetric> metrics, DateTime fetchedAtUtc);
    void Remove(string providerName);
    void Save();
    void Load();
}
```

`FileMetricCacheStore` implements it with an injectable file path (default
`%AppData%\AiMeter\cache.json`) so tests can use a temp directory. Register it as a singleton
in `App.xaml.cs` and inject it into `ProviderManager`.

### 3b. Schema

```json
{
  "version": 1,
  "providers": {
    "OpenCode": {
      "fetchedAtUtc": "2026-11-19T09:14:03.221Z",
      "metrics": [
        {
          "name": "OpenCode Weekly",
          "totalQuota": 100,
          "remainingQuota": 54,
          "resetTime": "2026-11-26T02:00:00",
          "windowDuration": "7.00:00:00",
          "isMoneyMetric": false,
          "isUnbounded": false,
          "usedAmount": null,
          "limitAmount": null,
          "balanceAmount": null,
          "currency": null
        }
      ]
    }
  }
}
```

- Write to `cache.json.tmp`, then `File.Move(..., overwrite: true)`; never leave a truncated
  cache after a crash.
- On any parse/version/write error: log, start empty (or keep the previous in-memory cache),
  continue polling.
- Do not serialize `DisplayName`/`CustomColor`/`IsStale`; those are app state, not fetched data.
- No expiry: the age is shown to the user. Entries are removed on logout, session expiry,
  no-data, or a successful empty result. This avoids silently discarding usable values during
  a long outage.

### 3c. Privacy docs

Add one row to `docs/privacy.md` and the site copy: “Last successful usage snapshot
(numbers, reset times, timestamp) — lets the widget show the last known values while offline —
local `%AppData%\AiMeter\cache.json`.”

---

## Section 4 — Stale indicator

### 4a. Model

`UsageMetric` gains:

```csharp
[ObservableProperty] private bool _isStale;
[ObservableProperty] private DateTime? _lastUpdatedUtc;
```

`UpdateFrom` copies both. `ProviderManager` stamps them; providers never set them.

### 4b. Age converter

`Converters/StaleAgeConverter.cs` (`IMultiValueConverter`, inputs `LastUpdatedUtc`, `Now`):

- `< 1 min` → `just now`
- `< 60 min` → `{n}m ago`
- `< 24 h` → `{n}h ago`
- otherwise → `{n}d ago`
- missing/both null → empty string

Pure function, unit-tested. `Now` already ticks every 30 seconds in `WidgetViewModel`.

### 4c. Compact + Deskband (`CompactMetricRow`)

- Label cell becomes a horizontal `StackPanel`: amber `⚠` (`#E6A700`, same font size as the
  label, margin `0,0,4,0`), visible only when `IsStale`; then the existing `DisplayName`.
- Bar fill gets `Opacity="0.45"` via a `DataTrigger IsStale=True`, preserving the quota hue.
- Tooltip adds a line, visible when `IsStale`:
  `⚠ Not updated — last successful update 5m ago`.
- No layout height change: the glyph sits inside the existing fixed-height row.

### 4d. Taskbar (`TaskbarMetricColumn`)

- 5×5 amber dot overlaid at the top-right of the 28px column, visible when `IsStale`.
- Fill opacity 0.45 under the same trigger.
- Same stale line in the tooltip.

### 4e. What does not change

- Placeholder tiles (`Auth Required`, `Error Fetching`, `Session Expired`, `No subscription`)
  keep their current look and are never marked stale.
- Bar color mode/converters are untouched; stale is an overlay, not a new color mode.
- Settings → Metrics still lists only real metric names.

---

## Section 5 — WebView2 init recovery (offline robustness)

`OpenCodeApiClient.EnsureInitializedAsync` must reset a faulted/cancelled init task, matching
`ClaudeApiClient`:

```csharp
private async Task EnsureInitializedAsync()
{
    if (_initTask is { IsFaulted: true } or { IsCanceled: true })
        _initTask = null;
    _initTask ??= InitializeAsync();
    await _initTask;
}
```

On the next poll after the network returns, the WebView initializes and the provider recovers
without restarting AiMeter. Also dispose a partially created host window/webview if
initialization fails, so retries do not leak hidden windows. This does not change the cache
logic but removes a permanent-failure path that looks like “data can’t be updated”.

---

## Section 6 — Logs folder button

### 6a. Shared path helper

`src/AiMeter/Logging/LogPaths.cs`:

```csharp
public static class LogPaths
{
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AiMeter", "logs");
}
```

`LoggingExtensions.AddFileLogger` uses `LogPaths.Directory` instead of its own string.

### 6b. Command and UI

`SettingsViewModel`:

```csharp
public string LogsFolderPath => LogPaths.Directory;

[RelayCommand]
private void OpenLogsFolder()
{
    Directory.CreateDirectory(LogsFolderPath);
    Process.Start(new ProcessStartInfo
    {
        FileName = LogsFolderPath,
        UseShellExecute = true
    });
}
```

Wrap in try/catch and log a warning; a failed shell launch must not crash Settings.

`SettingsWindow.xaml`, General tab, after the Accounts card:

```xml
<Border Style="{StaticResource SectionCard}">
    <StackPanel>
        <TextBlock Text="DIAGNOSTICS" Style="{StaticResource SectionHeader}" />
        <Button Content="Open logs folder"
                Command="{Binding OpenLogsFolderCommand}"
                Width="150" Height="30" HorizontalAlignment="Left"
                Background="#5B5B5B" Foreground="White" FontWeight="SemiBold"
                BorderThickness="0" Cursor="Hand" />
        <TextBlock Text="{Binding LogsFolderPath}" Style="{StaticResource HintText}" />
    </StackPanel>
</Border>
```

No tray-menu duplicate in this iteration; one discoverable button is enough.

---

## Files affected

| File | Change |
|---|---|
| `src/AiMeter/Providers/IProvider.cs` | return `ProviderFetchResult` |
| `src/AiMeter/Providers/ProviderFetchResult.cs` (new) | status enum + record |
| `src/AiMeter/Providers/ClaudeWebProvider.cs` | map failures to explicit statuses |
| `src/AiMeter/Providers/OpenCodeProvider.cs` | map failures to explicit statuses |
| `src/AiMeter/Managers/ProviderManager.cs` | shadow-cache state machine, restore, stale stamping, refresh lock |
| `src/AiMeter/Services/IMetricCacheStore.cs` (new) | cache interface + snapshot type |
| `src/AiMeter/Services/FileMetricCacheStore.cs` (new) | versioned JSON, atomic write/load |
| `src/AiMeter/Models/UsageMetric.cs` | `IsStale`, `LastUpdatedUtc`, `UpdateFrom` |
| `src/AiMeter/Converters/StaleAgeConverter.cs` (new) | relative age text |
| `src/AiMeter/Styles/WidgetStyles.xaml` | warning glyph/dot, fill opacity, tooltip line |
| `src/AiMeter/Services/OpenCodeApiClient.cs` | reset faulted init task |
| `src/AiMeter/Logging/LogPaths.cs` (new) | shared log directory |
| `src/AiMeter/Logging/LoggingExtensions.cs` | use `LogPaths` |
| `src/AiMeter/ViewModels/SettingsViewModel.cs` | logs-folder command/path |
| `src/AiMeter/Views/SettingsWindow.xaml` | Diagnostics card |
| `src/AiMeter/App.xaml.cs` | register cache store |
| `docs/privacy.md` | local cache row |
| `README.md`, `CHANGELOG.md`, `docs/index.html` | feature notes after implementation |
| tests | contract/fakes/cache/manager/converter tests |

---

## Verification

Unit tests:

1. `ProviderManager` replaces an `Error` result with the last successful metrics, sets
   `IsStale = true`, and preserves the original timestamp.
2. A later `Success` clears `IsStale`, advances `LastUpdatedUtc`, and drops metrics that are no
   longer returned.
3. `NoSession`, `SessionExpired`, and `NoData` remove the provider’s cache entry and do not show
   stale bars.
4. Cache round-trip: save a snapshot, load it into a fresh manager, verify metrics + timestamp,
   `HasFetchedOnce = true`, and all metrics stale.
5. Corrupt/absent/unsupported-version cache starts empty without throwing.
6. `RefilterMetrics` still re-filters both live and stale snapshots without a fetch.
7. Alerts do not fire from repeated stale snapshots or from restored cache on startup.
8. `StaleAgeConverter` boundaries: just now, 59m, 1h, 23h, 1d.
9. `ProviderFetchResult` classification tests for both providers (success, no session,
   expired, no subscription, error).

Manual:

1. Run with both providers working, wait for one successful poll, then block network access
   (disable the adapter or block `claude.ai` / `opencode.ai`).
2. Confirm bars remain, show ⚠ + dimmed fill, and the tooltip says how old the data is in
   Compact, Taskbar and Deskband.
3. Restart AiMeter while still offline; cached bars appear immediately, still stale.
4. Restore the network, press **Refresh now**; warning clears within one poll.
5. Log out of one provider; its cached bars disappear rather than showing stale.
6. Settings → General → **Open logs folder** opens Explorer at
   `%AppData%\AiMeter\logs` (directory created if missing).

Commands:

```
dotnet build AiMeter.slnx
dotnet test tests/AiMeter.Tests/AiMeter.Tests.csproj
```

## Open questions

- Should a session-expired provider keep its last bars with a “session expired” warning instead
  of clearing them? Planned answer: no — auth state wins, as today.
- Should the warning include a global “offline” badge when every provider is stale? Not in this
  iteration; per-metric tooltips already identify the condition.
- Should cached data expire after N days? Planned answer: no hard expiry; the age is visible and
  entries are invalidated by auth/no-data transitions.
