using System;
using System.Collections.Generic;
using AiMeter.Models;
using AiMeter.Services;

namespace AiMeter.Tests.Fakes;

/// <summary>
/// In-memory IMetricCacheStore. Tests can pre-seed <see cref="Entries"/> before constructing
/// the manager to simulate a cache restored from disk at startup.
/// </summary>
public sealed class FakeMetricCacheStore : IMetricCacheStore
{
    public Dictionary<string, CachedProviderMetrics> Entries { get; } = new(StringComparer.Ordinal);

    public int LoadCallCount { get; private set; }
    public int SaveCallCount { get; private set; }

    public bool TryGet(string providerName, out CachedProviderMetrics snapshot)
    {
        if (Entries.TryGetValue(providerName, out var found))
        {
            snapshot = found;
            return true;
        }

        snapshot = null!;
        return false;
    }

    public void Set(string providerName, IReadOnlyList<UsageMetric> metrics, DateTime fetchedAtUtc)
        => Entries[providerName] = new CachedProviderMetrics(metrics, fetchedAtUtc);

    public void Remove(string providerName) => Entries.Remove(providerName);

    public void Save() => SaveCallCount++;

    public void Load() => LoadCallCount++;
}
