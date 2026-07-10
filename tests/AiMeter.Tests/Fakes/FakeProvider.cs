using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AiMeter.Models;
using AiMeter.Providers;

namespace AiMeter.Tests.Fakes;

/// <summary>
/// In-memory IProvider for tests. Exposes the metric list it returns per call and a
/// counter so tests can assert whether a network fetch actually happened.
/// </summary>
public sealed class FakeProvider : IProvider
{
    public string Name { get; init; } = "Fake";

    public IReadOnlyList<UsageMetric> Metrics { get; set; } = new List<UsageMetric>();

    public int GetMetricsAsyncCallCount { get; private set; }

    public Task<IReadOnlyList<UsageMetric>> GetMetricsAsync()
    {
        GetMetricsAsyncCallCount++;
        return Task.FromResult(Metrics);
    }
}