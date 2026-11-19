using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using AiMeter.Models;
using AiMeter.Providers;
using AiMeter.Services;
using Microsoft.Extensions.Logging;

namespace AiMeter.Managers;

public partial class ProviderManager : ObservableObject, IProviderManager
{
    private readonly IEnumerable<IProvider> _providers;
    private readonly ISettingsManager _settingsManager;
    private readonly ILogger<ProviderManager>? _logger;
    private readonly IMetricCacheStore _cache;
    private readonly Dictionary<string, ProviderState> _stateByProvider = new();
    private readonly Dictionary<string, UsageMetric> _previousByMetricName = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;

    private sealed record ProviderState(
        IReadOnlyList<UsageMetric> Metrics,
        DateTime? LastSuccessUtc,
        bool IsStale);

    /// <summary>Stand-in used when no cache store is injected, so tests never touch %AppData%.</summary>
    private sealed class NullMetricCacheStore : IMetricCacheStore
    {
        public bool TryGet(string providerName, out CachedProviderMetrics snapshot)
        {
            snapshot = null!;
            return false;
        }

        public void Set(string providerName, IReadOnlyList<UsageMetric> metrics, DateTime fetchedAtUtc) { }
        public void Remove(string providerName) { }
        public void Save() { }
        public void Load() { }
    }

    public ObservableCollection<UsageMetric> Metrics { get; } = new();

    public ObservableCollection<string> KnownMetricNames { get; } = new();

    [ObservableProperty]
    private bool _hasFetchedOnce;

    public event EventHandler<QuotaAlert>? AlertRaised;

    public ProviderManager(
        IEnumerable<IProvider> providers,
        ISettingsManager settingsManager,
        ILogger<ProviderManager>? logger = null,
        IMetricCacheStore? cacheStore = null)
    {
        _providers = providers;
        _settingsManager = settingsManager;
        _logger = logger;
        _cache = cacheStore ?? new NullMetricCacheStore();

        _cache.Load();
        RestoreCachedSnapshots();
    }

    /// <summary>
    /// Publishes persisted snapshots immediately at startup so the widget shows the last known
    /// bars (marked stale) while the first poll runs. Never raises alerts.
    /// </summary>
    private void RestoreCachedSnapshots()
    {
        var anyRestored = false;

        foreach (var provider in _providers)
        {
            if (!_cache.TryGet(provider.Name, out var snapshot) || snapshot.Metrics.Count == 0)
            {
                continue;
            }

            foreach (var metric in snapshot.Metrics)
            {
                metric.IsStale = true;
                metric.LastUpdatedUtc = snapshot.FetchedAtUtc;
            }

            _stateByProvider[provider.Name] = new ProviderState(snapshot.Metrics, snapshot.FetchedAtUtc, true);
            anyRestored = true;
        }

        if (!anyRestored) return;

        TrackKnownMetricNames();
        SyncMetrics(SelectedMetrics());
        HasFetchedOnce = true;
    }

    public Task StartAsync()
    {
        if (_cts != null) return Task.CompletedTask;

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(_settingsManager.Current.PollingIntervalSeconds));

        // Start polling in background
        _ = PollLoopAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        _cts = null;
        return Task.CompletedTask;
    }

    public async Task RefreshAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            foreach (var provider in _providers)
            {
                var now = DateTime.UtcNow;

                ProviderFetchResult result;
                try
                {
                    result = await provider.GetMetricsAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Provider {Provider} fetch threw; treating as a transient error", provider.Name);
                    result = new ProviderFetchResult(
                        new List<UsageMetric> { new() { Name = $"{provider.Name} (Error Fetching)" } },
                        ProviderFetchStatus.Error);
                }

                switch (result.Status)
                {
                    case ProviderFetchStatus.Success:
                    {
                        var real = result.Metrics.Where(m => !IsPlaceholderMetric(m.Name)).ToList();

                        if (real.Count == 0)
                        {
                            _cache.Remove(provider.Name);
                            _stateByProvider[provider.Name] = new ProviderState(real, now, false);
                        }
                        else
                        {
                            foreach (var metric in real)
                            {
                                metric.LastUpdatedUtc = now;
                                metric.IsStale = false;
                            }

                            _stateByProvider[provider.Name] = new ProviderState(real, now, false);
                            _cache.Set(provider.Name, real, now);
                        }

                        break;
                    }

                    case ProviderFetchStatus.Error:
                    {
                        if (_cache.TryGet(provider.Name, out var snapshot) && snapshot.Metrics.Count > 0)
                        {
                            foreach (var metric in snapshot.Metrics)
                            {
                                metric.IsStale = true;
                                metric.LastUpdatedUtc = snapshot.FetchedAtUtc;
                            }

                            _stateByProvider[provider.Name] = new ProviderState(
                                snapshot.Metrics,
                                snapshot.FetchedAtUtc,
                                true);
                        }
                        else
                        {
                            // No cache yet: keep today's error-tile behavior.
                            _stateByProvider[provider.Name] = new ProviderState(result.Metrics, null, false);
                        }

                        break;
                    }

                    default:
                        // NoSession / SessionExpired / NoData: showing cached bars for a
                        // logged-out or empty account would be misleading, so drop the cache.
                        _cache.Remove(provider.Name);
                        _stateByProvider[provider.Name] = new ProviderState(result.Metrics, null, false);
                        break;
                }
            }

            TrackKnownMetricNames();
            DetectAlerts(SelectedMetrics(), _settingsManager.Current);
            SyncMetrics(SelectedMetrics());
            HasFetchedOnce = true;
            _cache.Save();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Re-filters the cached last-good data against the current selection with no network
    /// call. Used after a Settings save so the new metric selection takes effect instantly;
    /// alerts are intentionally skipped (no new readings, just a re-filter).
    /// </summary>
    public void RefilterMetrics()
    {
        SyncMetrics(SelectedMetrics());
    }

    public static bool IsPlaceholderMetric(string name)
    {
        return name.Contains("Error", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Required", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Expired", StringComparison.OrdinalIgnoreCase)
            || name.Contains("No Workspace", StringComparison.OrdinalIgnoreCase)
            || name.Contains("No Subscription", StringComparison.OrdinalIgnoreCase)
            || name.Contains("No Usage Data", StringComparison.OrdinalIgnoreCase);
    }

    private List<UsageMetric> SelectedMetrics()
    {
        var config = _settingsManager.Current;
        var all = _stateByProvider.Values.SelectMany(s => s.Metrics).ToList();
        var realMetrics = all.Where(m => !IsPlaceholderMetric(m.Name)).ToList();

        config.SelectedMetrics.RemoveAll(IsPlaceholderMetric);

        var source = realMetrics.Count > 0 ? realMetrics : all;

        if (config.SelectedMetrics.Count == 0)
        {
            return source;
        }

        var filtered = source.Where(m => config.SelectedMetrics.Contains(m.Name)).ToList();
        return filtered.Count > 0 ? filtered : source;
    }

    private void TrackKnownMetricNames()
    {
        var config = _settingsManager.Current;

        for (var i = KnownMetricNames.Count - 1; i >= 0; i--)
        {
            if (IsPlaceholderMetric(KnownMetricNames[i]))
            {
                KnownMetricNames.RemoveAt(i);
            }
        }
        config.SelectedMetrics.RemoveAll(IsPlaceholderMetric);

        // Settings from before SeenMetrics existed: providers the user already picked metrics
        // from keep those choices; any provider with nothing selected (e.g. one logged into
        // after the first run filled SelectedMetrics) gets its metrics auto-selected.
        var migrating = config.SeenMetrics is null;
        var seen = config.SeenMetrics ??= new List<string>();

        foreach (var state in _stateByProvider.Values)
        {
            var real = state.Metrics.Where(m => !IsPlaceholderMetric(m.Name)).ToList();
            var keepChoices = migrating && real.Any(m => config.SelectedMetrics.Contains(m.Name));

            foreach (var metric in real)
            {
                if (!KnownMetricNames.Contains(metric.Name))
                {
                    KnownMetricNames.Add(metric.Name);
                }

                if (seen.Contains(metric.Name)) continue;
                seen.Add(metric.Name);

                // First sighting: show it. Without this, once SelectedMetrics is non-empty a
                // newly logged-in provider's metrics would stay hidden and unchecked.
                if (!keepChoices && !config.SelectedMetrics.Contains(metric.Name))
                {
                    config.SelectedMetrics.Add(metric.Name);
                }
            }
        }
    }

    private void DetectAlerts(List<UsageMetric> latest, AppConfig config)
    {
        if (!config.NotificationsEnabled) return;

        foreach (var metric in latest)
        {
            // Unbounded money metrics (Claude Extra Usage on an "Unlimited" plan) always
            // report RemainingPercentage=100 since there's no cap to measure against -
            // skip them so they don't fire a spurious "reset" alert every single poll.
            if (metric.IsUnbounded) continue;

            _previousByMetricName.TryGetValue(metric.Name, out var previous);
            var currentPct = metric.RemainingPercentage;

            if (previous is not null)
            {
                var previousPct = previous.RemainingPercentage;

                if (config.NotifyOnExhausted && previousPct > 0 && currentPct <= 0)
                {
                    AlertRaised?.Invoke(this, new QuotaAlert($"{metric.Name} exhausted", "Remaining quota has reached 0%."));
                }
                else if (config.NotifyOnLowQuota && previousPct > config.LowQuotaThreshold && currentPct <= config.LowQuotaThreshold)
                {
                    AlertRaised?.Invoke(this, new QuotaAlert($"{metric.Name} running low", $"Remaining quota is at {currentPct:0}%."));
                }
                else if (config.NotifyOnReset && previousPct < 100 && currentPct >= 100)
                {
                    AlertRaised?.Invoke(this, new QuotaAlert($"{metric.Name} reset", "Quota has been reset to 100%."));
                }
            }

            _previousByMetricName[metric.Name] = metric;
        }
    }

    /// <summary>
    /// Updates Metrics to match latest without ever clearing the whole collection,
    /// so the widget keeps showing cached values while a refresh is in flight.
    /// </summary>
    private void SyncMetrics(List<UsageMetric> latest)
    {
        for (var i = Metrics.Count - 1; i >= 0; i--)
        {
            if (!latest.Any(m => m.Name == Metrics[i].Name))
            {
                Metrics.RemoveAt(i);
            }
        }

        for (var i = 0; i < latest.Count; i++)
        {
            var newMetric = latest[i];
            var existingIndex = IndexOfByName(newMetric.Name);

            if (existingIndex == -1)
            {
                Metrics.Insert(Math.Min(i, Metrics.Count), newMetric);
            }
            else
            {
                if (existingIndex != i)
                {
                    Metrics.Move(existingIndex, i);
                }

                // Update the existing instance's properties instead of replacing the
                // collection entry, so bound UI (bars, labels) refreshes via
                // INotifyPropertyChanged rather than the ItemsControl tearing down and
                // recreating the tile's elements on every poll.
                Metrics[i].UpdateFrom(newMetric);
            }

            // Overlay the user's per-metric label/color overrides. Applied to the live
            // Metrics entry (whether just inserted or updated in place) rather than in
            // UpdateFrom, since these come from settings, not the provider payload.
            ApplyUserCustomizations(Metrics[i]);
        }
    }

    /// <summary>
    /// Resolves a metric's user-facing DisplayName and CustomColor from the current settings'
    /// per-metric override maps. Falls back to the metric's own Name / the quota palette (null)
    /// when no override is set.
    /// </summary>
    private void ApplyUserCustomizations(UsageMetric metric)
    {
        var config = _settingsManager.Current;

        metric.DisplayName = config.MetricLabels.TryGetValue(metric.Name, out var label)
            && !string.IsNullOrWhiteSpace(label)
                ? label
                : metric.Name;

        metric.CustomColor = config.MetricColors.TryGetValue(metric.Name, out var color)
            && !string.IsNullOrWhiteSpace(color)
                ? color
                : null;
    }

    private int IndexOfByName(string name)
    {
        for (var i = 0; i < Metrics.Count; i++)
        {
            if (Metrics[i].Name == name) return i;
        }
        return -1;
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        // Initial fetch
        await RefreshAsync();

        try
        {
            while (await _timer!.WaitForNextTickAsync(token))
            {
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
    }
}
