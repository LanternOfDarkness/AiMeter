using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using AiMeter.Models;
using AiMeter.Providers;

namespace AiMeter.Managers;

public partial class ProviderManager : ObservableObject, IProviderManager
{
    private readonly IEnumerable<IProvider> _providers;
    private readonly ISettingsManager _settingsManager;
    private readonly Dictionary<string, IReadOnlyList<UsageMetric>> _lastGoodByProvider = new();
    private readonly Dictionary<string, UsageMetric> _previousByMetricName = new();
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;

    public ObservableCollection<UsageMetric> Metrics { get; } = new();

    public event EventHandler<QuotaAlert>? AlertRaised;

    public ProviderManager(IEnumerable<IProvider> providers, ISettingsManager settingsManager)
    {
        _providers = providers;
        _settingsManager = settingsManager;
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
            catch (Exception)
            {
                // Keep this provider's last-good snapshot; one failing provider
                // should not blank out the metrics for every other provider.
            }
        }

        var config = _settingsManager.Current;
        var all = _lastGoodByProvider.Values.SelectMany(m => m);
        var latest = config.SelectedMetrics.Count == 0
            ? all.ToList()
            : all.Where(m => config.SelectedMetrics.Contains(m.Name)).ToList();

        DetectAlerts(latest, config);
        SyncMetrics(latest);
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
                Metrics[i] = newMetric;
            }
        }
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
