using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AiMeter.Models;
using AiMeter.Services;

namespace AiMeter.Providers;

public class ClaudeWebProvider : IProvider
{
    public string Name => "Claude Web";

    private readonly IClaudeSession _session;
    private readonly IClaudeApiClient _apiClient;

    public ClaudeWebProvider(IClaudeSession session, IClaudeApiClient apiClient)
    {
        _session = session;
        _apiClient = apiClient;
    }

    public async Task<IReadOnlyList<UsageMetric>> GetMetricsAsync()
    {
        var metrics = new List<UsageMetric>();

        if (!_session.HasSession)
        {
            metrics.Add(new UsageMetric { Name = "Auth Required", TotalQuota = 100, RemainingQuota = 0 });
            return metrics;
        }

        try
        {
            var (orgsStatus, orgsBody) = await _apiClient.GetAsync("/api/organizations");
            if (orgsStatus is 401 or 403)
            {
                _session.Clear();
                metrics.Add(new UsageMetric { Name = "Session Expired", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }
            if (orgsStatus != 200 || orgsBody is null)
            {
                metrics.Add(new UsageMetric { Name = "Error Fetching", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }

            using var orgsDoc = JsonDocument.Parse(orgsBody);
            var orgId = orgsDoc.RootElement[0].GetProperty("uuid").GetString();

            var (usageStatus, usageBody) = await _apiClient.GetAsync($"/api/organizations/{orgId}/usage");
            if (usageStatus != 200 || usageBody is null)
            {
                metrics.Add(new UsageMetric { Name = "Error Fetching", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }

            metrics.AddRange(ParseUsage(usageBody));
        }
        catch (Exception)
        {
            metrics.Add(new UsageMetric { Name = "Error Fetching", TotalQuota = 100, RemainingQuota = 0 });
        }

        return metrics;
    }

    /// <summary>
    /// claude.ai's /usage response carries the same data twice: loose top-level fields
    /// (many null, some under obfuscated codenames) and a "limits" array of
    /// { kind, percent, resets_at, scope }. The array is the stable, forward-compatible
    /// shape - new limit kinds (e.g. per-model weekly caps) show up automatically.
    /// </summary>
    private static IEnumerable<UsageMetric> ParseUsage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var limit in limits.EnumerateArray())
        {
            var kind = limit.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() : null;
            var percentUsed = limit.TryGetProperty("percent", out var percentProp) ? percentProp.GetDouble() : 0;
            DateTime? resetsAt = limit.TryGetProperty("resets_at", out var resetsProp) && resetsProp.ValueKind == JsonValueKind.String
                ? resetsProp.GetDateTimeOffset().ToLocalTime().DateTime
                : null;

            yield return new UsageMetric
            {
                Name = NameForLimit(kind, limit),
                TotalQuota = 100,
                RemainingQuota = Math.Max(0, 100 - percentUsed),
                ResetTime = resetsAt
            };
        }
    }

    private static string NameForLimit(string? kind, JsonElement limit) => kind switch
    {
        "session" => "Claude Session",
        "weekly_all" => "Claude Weekly",
        "weekly_scoped" => ScopedModelName(limit) is { } model ? $"Claude Weekly ({model})" : "Claude Weekly (Scoped)",
        null => "Claude",
        _ => $"Claude {kind}"
    };

    private static string? ScopedModelName(JsonElement limit)
    {
        if (limit.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object
            && scope.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object
            && model.TryGetProperty("display_name", out var displayName) && displayName.ValueKind == JsonValueKind.String)
        {
            return displayName.GetString();
        }
        return null;
    }
}
