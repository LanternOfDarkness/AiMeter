using System;
using System.Collections.Generic;
using AiMeter.Models;

namespace AiMeter.Services;

/// <summary>A provider's last successful metric snapshot and when it was fetched (UTC).</summary>
public sealed record CachedProviderMetrics(IReadOnlyList<UsageMetric> Metrics, DateTime FetchedAtUtc);

/// <summary>
/// Persists the last successful per-provider metrics so the widget can show them while a
/// fetch is failing and after a restart. Implementations never throw from <c>Load</c>/<c>Save</c>.
/// </summary>
public interface IMetricCacheStore
{
    bool TryGet(string providerName, out CachedProviderMetrics snapshot);
    void Set(string providerName, IReadOnlyList<UsageMetric> metrics, DateTime fetchedAtUtc);
    void Remove(string providerName);
    void Save();
    void Load();
}
