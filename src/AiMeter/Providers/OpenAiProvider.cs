using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AiMeter.Models;
using AiMeter.Services;
using Microsoft.Extensions.Logging;

namespace AiMeter.Providers;

/// <summary>
/// Reports OpenAI Platform (platform.openai.com) prepaid-credit balance as a usage meter. Unlike
/// the ChatGPT consumer app, the Platform exposes a real passive balance endpoint, and unlike the
/// browser-session providers it authenticates with an API key the user pastes in Settings (stored
/// encrypted via <see cref="OpenAiSession"/>). Until a key is configured this makes no network call.
///
/// Source: GET /v1/dashboard/billing/credit_grants (Authorization: Bearer &lt;key&gt;) →
///   { total_granted, total_used, total_available, grants:{ data:[…] } }
/// total_available/total_granted is the remaining fraction — a natural quota ring. The endpoint is
/// the OpenAI dashboard's own (undocumented) call; it needs a user/admin key with billing read
/// access (project keys get 403), matching how the CodexBar menu-bar app reads the same balance.
/// </summary>
public class OpenAiProvider : IProvider
{
    public string Name => "OpenAI";

    private const string CreditGrantsEndpoint = "/v1/dashboard/billing/credit_grants";

    private readonly IOpenAiSession _session;
    private readonly IOpenAiApiClient _apiClient;
    private readonly ILogger<OpenAiProvider> _logger;

    public OpenAiProvider(IOpenAiSession session, IOpenAiApiClient apiClient, ILogger<OpenAiProvider> logger)
    {
        _session = session;
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UsageMetric>> GetMetricsAsync()
    {
        var metrics = new List<UsageMetric>();

        var apiKey = _session.GetApiKey();
        if (!_session.HasSession || string.IsNullOrEmpty(apiKey))
        {
            metrics.Add(new UsageMetric { Name = "OpenAI (API Key Required)", TotalQuota = 100, RemainingQuota = 0 });
            return metrics;
        }

        try
        {
            var (status, body) = await _apiClient.GetAsync(CreditGrantsEndpoint, apiKey);

            // The key is user-managed, so an auth failure is a bad/insufficient key, not an expired
            // session — surface a clear message but never silently clear what the user typed.
            var authError = status switch
            {
                401 => "OpenAI (Invalid Key)",
                403 => "OpenAI (No Billing Access)",  // project key, or key without billing scope
                404 => "OpenAI (Billing Unavailable)", // endpoint not offered for this account
                _ => null,
            };
            if (authError is not null)
            {
                _logger.LogWarning("OpenAI credit_grants returned {Status}", status);
                metrics.Add(new UsageMetric { Name = authError, TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }
            if (status != 200 || body is null)
            {
                _logger.LogWarning("OpenAI credit_grants request failed with status {Status}", status);
                metrics.Add(new UsageMetric { Name = "OpenAI (Error Fetching)", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }

            var credit = ParseCreditGrants(body);
            if (credit is null)
            {
                _logger.LogWarning("OpenAI credit_grants had no usable balance ({Length} chars).", body.Length);
                metrics.Add(new UsageMetric { Name = "OpenAI (No Credits)", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }

            metrics.Add(credit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI usage fetch threw an unhandled exception");
            metrics.Add(new UsageMetric { Name = "OpenAI (Error Fetching)", TotalQuota = 100, RemainingQuota = 0 });
        }

        return metrics;
    }

    /// <summary>
    /// Maps a credit_grants payload to an "OpenAI Credits" metric (TotalQuota = total_granted,
    /// RemainingQuota = total_available). Returns null when the account carries no granted credits
    /// (total_granted ≤ 0) — a 0/0 ring would misleadingly read as "exhausted" for pay-as-you-go
    /// accounts that simply don't use prepaid credits.
    /// </summary>
    private static UsageMetric? ParseCreditGrants(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var granted = GetNumber(root, "total_granted");
        var available = GetNumber(root, "total_available");
        if (granted is not > 0) return null;

        return new UsageMetric
        {
            Name = "OpenAI Credits",
            TotalQuota = granted.Value,
            RemainingQuota = Math.Clamp(available ?? 0, 0, granted.Value),
        };
    }

    private static double? GetNumber(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDouble()
            : null;
}
