using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;

    public ObservableCollection<UsageMetric> Metrics { get; } = new();

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
        Metrics.Clear();
        foreach (var provider in _providers)
        {
            var results = await provider.GetMetricsAsync();
            foreach (var metric in results)
            {
                Metrics.Add(metric);
            }
        }
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
