using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AiMeter.Models;
using AiMeter.Services;
using Microsoft.Extensions.Logging;

namespace AiMeter.Providers;

public class ClaudeWebProvider : IProvider
{
    public string Name => "Claude Web";

    private readonly IClaudeSession _session;
    private readonly IClaudeApiClient _apiClient;
    private readonly ILogger<ClaudeWebProvider> _logger;

    public ClaudeWebProvider(IClaudeSession session, IClaudeApiClient apiClient, ILogger<ClaudeWebProvider> logger)
    {
        _session = session;
        _apiClient = apiClient;
        _logger = logger;
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
                _logger.LogWarning("Claude session expired (status {Status}); clearing", orgsStatus);
                _session.Clear();
                metrics.Add(new UsageMetric { Name = "Session Expired", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }
            if (orgsStatus != 200 || orgsBody is null)
            {
                _logger.LogWarning("Claude orgs request failed with status {Status}", orgsStatus);
                metrics.Add(new UsageMetric { Name = "Error Fetching", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }

            using var orgsDoc = JsonDocument.Parse(orgsBody);
            var orgId = orgsDoc.RootElement[0].GetProperty("uuid").GetString();

            var (usageStatus, usageBody) = await _apiClient.GetAsync($"/api/organizations/{orgId}/usage");
            if (usageStatus != 200 || usageBody is null)
            {
                _logger.LogWarning("Claude usage request for org {OrgId} failed with status {Status}", orgId, usageStatus);
                metrics.Add(new UsageMetric { Name = "Error Fetching", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }

            _logger.LogInformation("Claude usage response length: {Length}, body: {Body}", usageBody.Length, usageBody);
            metrics.AddRange(ParseUsage(usageBody));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude usage fetch threw an unhandled exception");
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
        var yieldedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (doc.RootElement.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var limit in limits.EnumerateArray())
            {
                var kind = limit.TryGetProperty("kind", out var kindProp) && kindProp.ValueKind == JsonValueKind.String
                    ? kindProp.GetString()
                    : null;
                
                double percentUsed = 0;
                if (limit.TryGetProperty("percent", out var percentProp) && percentProp.ValueKind == JsonValueKind.Number)
                {
                    percentUsed = percentProp.GetDouble();
                }

                DateTime? resetsAt = limit.TryGetProperty("resets_at", out var resetsProp) && resetsProp.ValueKind == JsonValueKind.String
                    ? resetsProp.GetDateTimeOffset().ToLocalTime().DateTime
                    : null;

                var name = NameForLimit(kind, limit);
                yieldedNames.Add(name);

                yield return new UsageMetric
                {
                    Name = name,
                    TotalQuota = 100,
                    RemainingQuota = Math.Max(0, 100 - percentUsed),
                    ResetTime = resetsAt,
                    WindowDuration = WindowDurationForKind(kind)
                };
            }
        }

        if (!yieldedNames.Contains("Claude Extra Usage"))
        {
            double usedInDollars = 0;
            double limitInDollars = 0;
            double percentUsed = 0;
            bool hasExtraUsageData = false;

            if (doc.RootElement.TryGetProperty("extra_usage", out var extraUsage) && extraUsage.ValueKind == JsonValueKind.Object)
            {
                bool isEnabled = !extraUsage.TryGetProperty("is_enabled", out var enabledProp) || enabledProp.ValueKind != JsonValueKind.False;
                if (isEnabled)
                {
                    hasExtraUsageData = true;
                    double decPlaces = 2;
                    if (TryGetDoubleVal(extraUsage, "decimal_places", out var dp) && dp >= 0) decPlaces = dp;
                    double divisor = Math.Pow(10, decPlaces);

                    if (TryGetDoubleVal(extraUsage, "used_credits", out var uc))
                        usedInDollars = uc / divisor;
                    else if (TryGetDoubleVal(extraUsage, "used", out var u))
                        usedInDollars = u;
                    else if (TryGetDoubleVal(extraUsage, "used_cents", out var uc2))
                        usedInDollars = uc2 / 100.0;

                    if (TryGetDoubleVal(extraUsage, "monthly_limit", out var ml))
                        limitInDollars = ml;
                    else if (TryGetDoubleVal(extraUsage, "limit", out var l))
                        limitInDollars = l;
                    else if (TryGetDoubleVal(extraUsage, "limit_cents", out var lc))
                        limitInDollars = lc / 100.0;

                    if (TryGetDoubleVal(extraUsage, "utilization", out var ut))
                        percentUsed = ut;
                    else if (TryGetDoubleVal(extraUsage, "percent", out var p))
                        percentUsed = p;
                }
            }

            if (doc.RootElement.TryGetProperty("spend", out var spend) && spend.ValueKind == JsonValueKind.Object)
            {
                hasExtraUsageData = true;
                if (spend.TryGetProperty("used", out var spendUsed) && spendUsed.ValueKind == JsonValueKind.Object)
                {
                    double exp = 2;
                    if (TryGetDoubleVal(spendUsed, "exponent", out var e) && e >= 0) exp = e;
                    if (TryGetDoubleVal(spendUsed, "amount_minor", out var am))
                        usedInDollars = am / Math.Pow(10, exp);
                }

                if (TryGetDoubleVal(spend, "limit", out var sl))
                    limitInDollars = sl;

                if (TryGetDoubleVal(spend, "percent", out var sp))
                    percentUsed = sp;
            }

            if (hasExtraUsageData)
            {
                if (limitInDollars > 0)
                {
                    percentUsed = Math.Min(100.0, (usedInDollars / limitInDollars) * 100.0);
                }

                yield return new UsageMetric
                {
                    Name = "Claude Extra Usage",
                    TotalQuota = 100,
                    RemainingQuota = Math.Max(0, 100 - percentUsed),
                    WindowDuration = TimeSpan.FromDays(30)
                };
            }
        }

        if (!yieldedNames.Contains("Claude Balance") && (doc.RootElement.TryGetProperty("balance", out var balObj) || doc.RootElement.TryGetProperty("account_balance", out balObj)) && balObj.ValueKind == JsonValueKind.Object)
        {
            if (TryGetDoubleVal(balObj, "amount", out var amt) || TryGetDoubleVal(balObj, "balance", out amt) || TryGetDoubleVal(balObj, "available", out amt))
            {
                double limit = 100;
                if (TryGetDoubleVal(balObj, "limit", out var l) && l > 0) limit = l;
                double percentRemaining = limit > 0 ? Math.Min(100.0, (amt / limit) * 100.0) : 100.0;

                yield return new UsageMetric
                {
                    Name = "Claude Balance",
                    TotalQuota = 100,
                    RemainingQuota = Math.Max(0, percentRemaining),
                    WindowDuration = TimeSpan.FromDays(30)
                };
            }
        }
    }

    private static bool TryGetDoubleVal(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                value = prop.GetDouble();
                return true;
            }
            if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }
        }
        return false;
    }

    private static TimeSpan? WindowDurationForKind(string? kind) => kind switch
    {
        "session" => TimeSpan.FromHours(5),
        "weekly_all" or "weekly_scoped" => TimeSpan.FromDays(7),
        "extra_usage" or "extra_usage_dollars" or "monthly_spend" or "balance" => TimeSpan.FromDays(30),
        _ => null
    };

    private static string NameForLimit(string? kind, JsonElement limit) => kind switch
    {
        "session" => "Claude Session",
        "weekly_all" => "Claude Weekly",
        "weekly_scoped" => ScopedModelName(limit) is { } model ? $"Claude Weekly ({model})" : "Claude Weekly (Scoped)",
        "extra_usage" or "extra_usage_dollars" or "monthly_spend" => "Claude Extra Usage",
        "balance" or "credit_balance" or "current_balance" => "Claude Balance",
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
