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
        var money = ParseExtraUsageMoney(doc.RootElement);

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

                var metric = new UsageMetric
                {
                    Name = name,
                    TotalQuota = 100,
                    RemainingQuota = Math.Max(0, 100 - percentUsed),
                    ResetTime = resetsAt,
                    WindowDuration = WindowDurationForKind(kind)
                };

                // The limits[] array's own percent already reflects the plan's chosen quota
                // shape (bounded or not), so it stays the source of truth for RemainingQuota
                // here - money only adds the dollar figures for display.
                if (money is not null && name.Equals("Claude Extra Usage", StringComparison.Ordinal))
                {
                    ApplyMoney(metric, money);
                }

                yield return metric;
            }
        }

        if (!yieldedNames.Contains("Claude Extra Usage") && money is not null)
        {
            yield return BuildExtraUsageMetric(money);
        }
    }

    /// <summary>
    /// Pulls Extra Usage's spend figures from wherever the payload puts them. Two overlapping
    /// sources exist: the legacy top-level <c>extra_usage</c> object (numbers scaled by its own
    /// <c>decimal_places</c>) and the newer <c>spend</c> object (money as {amount_minor,
    /// exponent} pairs, plus an authoritative <c>percent</c> and optional prepaid
    /// <c>balance</c>/<c>cap</c>). Later sources win on conflict since they're the more current
    /// shape; <c>extra_usage.utilization</c> is kept as the percent of record when present since
    /// it carries more decimal precision than <c>spend.percent</c>'s rounded integer.
    /// </summary>
    private static ExtraUsageMoney? ParseExtraUsageMoney(JsonElement root)
    {
        double? used = null, limit = null, balance = null, percent = null;
        string? currency = null;
        bool hasData = false;

        if (root.TryGetProperty("extra_usage", out var extraUsage) && extraUsage.ValueKind == JsonValueKind.Object
            && (!extraUsage.TryGetProperty("is_enabled", out var enabledProp) || enabledProp.ValueKind != JsonValueKind.False))
        {
            hasData = true;
            double decPlaces = 2;
            if (TryGetDoubleVal(extraUsage, "decimal_places", out var dp) && dp >= 0) decPlaces = dp;
            double divisor = Math.Pow(10, decPlaces);

            if (TryGetDoubleVal(extraUsage, "used_credits", out var uc))
                used = uc / divisor;
            else if (TryGetDoubleVal(extraUsage, "used", out var u))
                used = u;
            else if (TryGetDoubleVal(extraUsage, "used_cents", out var uc2))
                used = uc2 / 100.0;

            if (TryGetDoubleVal(extraUsage, "monthly_limit", out var ml))
                limit = ml / divisor;
            else if (TryGetDoubleVal(extraUsage, "limit", out var l))
                limit = l;
            else if (TryGetDoubleVal(extraUsage, "limit_cents", out var lc))
                limit = lc / 100.0;

            if (TryGetDoubleVal(extraUsage, "utilization", out var ut))
                percent = ut;
            else if (TryGetDoubleVal(extraUsage, "percent", out var p))
                percent = p;

            if (extraUsage.TryGetProperty("currency", out var curProp) && curProp.ValueKind == JsonValueKind.String)
                currency = curProp.GetString();
        }

        if (root.TryGetProperty("spend", out var spend) && spend.ValueKind == JsonValueKind.Object
            && (!spend.TryGetProperty("enabled", out var spendEnabledProp) || spendEnabledProp.ValueKind != JsonValueKind.False))
        {
            hasData = true;

            if (TryGetMoney(spend, "used", out var spendUsed, out var usedCurrency))
            {
                used = spendUsed;
                currency ??= usedCurrency;
            }

            if (TryGetMoney(spend, "limit", out var spendLimit, out var limitCurrency))
            {
                limit = spendLimit;
                currency ??= limitCurrency;
            }
            else if (spend.TryGetProperty("cap", out var cap) && cap.ValueKind == JsonValueKind.Object)
            {
                if (TryGetMoney(cap, "money", out var capMoney, out var capMoneyCurrency))
                {
                    limit = capMoney;
                    currency ??= capMoneyCurrency;
                }
                else if (TryGetMoney(cap, "credits", out var capCredits, out var capCreditsCurrency))
                {
                    limit = capCredits;
                    currency ??= capCreditsCurrency;
                }
            }

            if (TryGetMoney(spend, "balance", out var spendBalance, out var balanceCurrency))
            {
                balance = spendBalance;
                currency ??= balanceCurrency;
            }

            // Only fills in when extra_usage.utilization above didn't already supply a
            // (more precise) percent.
            if (percent is null && TryGetDoubleVal(spend, "percent", out var sp))
                percent = sp;
        }

        return hasData ? new ExtraUsageMoney(used, limit, balance, percent, currency) : null;
    }

    private static void ApplyMoney(UsageMetric metric, ExtraUsageMoney money)
    {
        metric.IsMoneyMetric = true;
        metric.IsUnbounded = money.IsUnbounded;
        metric.UsedAmount = money.Used;
        metric.LimitAmount = money.Limit is > 0 ? money.Limit : null;
        metric.BalanceAmount = money.Balance;
        metric.Currency = money.Currency;
    }

    private static UsageMetric BuildExtraUsageMetric(ExtraUsageMoney money)
    {
        double remainingQuota;
        if (money.IsUnbounded)
        {
            // No cap to measure against - never render as "0% left"; the widget hides the
            // bar entirely for unbounded money metrics (see IsUnbounded) and shows spend/
            // balance text instead.
            remainingQuota = 100;
        }
        else
        {
            var percentUsed = money.Percent
                ?? (money.Limit is > 0 && money.Used is not null
                    ? Math.Min(100.0, (money.Used.Value / money.Limit.Value) * 100.0)
                    : 0.0);
            remainingQuota = Math.Max(0, 100 - percentUsed);
        }

        var metric = new UsageMetric
        {
            Name = "Claude Extra Usage",
            TotalQuota = 100,
            RemainingQuota = remainingQuota,
            WindowDuration = TimeSpan.FromDays(30)
        };
        ApplyMoney(metric, money);
        return metric;
    }

    /// <summary>Parsed Extra Usage figures in major currency units (e.g. dollars), merged from whichever payload fields supplied them.</summary>
    private sealed record ExtraUsageMoney(double? Used, double? Limit, double? Balance, double? Percent, string? Currency)
    {
        /// <summary>No monthly cap and no server-reported percent - Claude's "Unlimited" Extra Usage plan.</summary>
        public bool IsUnbounded => Limit is not > 0 && Percent is null;
    }

    /// <summary>Reads a {amount_minor, exponent, currency} money object into major units. Returns false for missing/null/non-object values.</summary>
    private static bool TryGetMoney(JsonElement parent, string propertyName, out double major, out string? currency)
    {
        major = 0;
        currency = null;

        if (!parent.TryGetProperty(propertyName, out var val) || val.ValueKind != JsonValueKind.Object)
            return false;

        if (!TryGetDoubleVal(val, "amount_minor", out var minor))
            return false;

        double exp = 2;
        if (TryGetDoubleVal(val, "exponent", out var e) && e >= 0) exp = e;

        major = minor / Math.Pow(10, exp);

        if (val.TryGetProperty("currency", out var curProp) && curProp.ValueKind == JsonValueKind.String)
            currency = curProp.GetString();

        return true;
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
