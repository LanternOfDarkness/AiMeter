using System.Collections.Generic;
using AiMeter.Models;

namespace AiMeter.Providers;

public enum ProviderFetchStatus
{
    Success,
    NoSession,
    SessionExpired,
    NoData,
    Error
}

public sealed record ProviderFetchResult(
    IReadOnlyList<UsageMetric> Metrics,
    ProviderFetchStatus Status);
