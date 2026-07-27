using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using AiMeter.Models;
using AiMeter.Providers;
using Microsoft.Extensions.Logging;

namespace AiMeter.Managers;

public partial class ProviderManager : ObservableObject, IProviderManager
{
    private readonly IEnumerable<IProvider> _providers;
    private readonly ISettingsManager _settingsManager;
    private readonly ILogger<ProviderManager>? _logger;
    private readonly Dictionary<string, IReadOnlyList<UsageMetric>> _lastGoodByProvider = new();
    private readonly Dictionary<string, UsageMetric> _previousByMetricName = new();
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;

    public ObservableCollection<UsageMetric> Metrics { get; } = new();

    public ObservableCollection<string> KnownMetricNames { get; } = new();

    [ObservableProperty]
    private bool _hasFetchedOnce;

    public event EventHandler<QuotaAlert>? AlertRaised;

    public ProviderManager(IEnumerable<IProvider> providers, ISettingsManager settingsManager, ILogger<ProviderManager>? logger = null)
    {
        _providers = providers;
        _settingsManager = settingsManager;
        _logger = logger;
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
        foreach (var provider in _providers)
        {
            try
            {
                _lastGoodByProvider[provider.Name] = await provider.GetMetricsAsync();
            }
            catch (Exception ex)
            {
                // Keep this provider's last-good snapshot; one failing provider
                // should not blank out the metrics for every other provider.
                _logger?.LogWarning(ex, "Provider {Provider} fetch failed; keeping last-good snapshot", provider.Name);
            }
        }

        TrackKnownMetricNames();
        DetectAlerts(SelectedMetrics(), _settingsManager.Current);
        SyncMetrics(SelectedMetrics());
        HasFetchedOnce = true;
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
            || name.Contains("No Usage Data", StringComparison.OrdinalIgnoreCase);
    }

    private List<UsageMetric> SelectedMetrics()
    {
        var config = _settingsManager.Current;
        var all = _lastGoodByProvider.Values.SelectMany(m => m).ToList();
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
        var all = _lastGoodByProvider.Values.SelectMany(m => m).ToList();
        var config = _settingsManager.Current;

        for (var i = KnownMetricNames.Count - 1; i >= 0; i--)
        {
            if (IsPlaceholderMetric(KnownMetricNames[i]))
            {
                KnownMetricNames.RemoveAt(i);
            }
        }
        config.SelectedMetrics.RemoveAll(IsPlaceholderMetric);

        var wasEmpty = config.SelectedMetrics.Count == 0;

        foreach (var metric in all.Where(m => !IsPlaceholderMetric(m.Name)))
        {
            if (!KnownMetricNames.Contains(metric.Name))
            {
                KnownMetricNames.Add(metric.Name);
                if (wasEmpty && !config.SelectedMetrics.Contains(metric.Name))
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
