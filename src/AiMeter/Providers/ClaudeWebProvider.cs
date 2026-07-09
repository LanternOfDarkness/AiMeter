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

#if DEBUG
            try
            {
                var dumpPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AiMeter", "usage_schema_debug.json");
                System.IO.File.WriteAllText(dumpPath, usageBody);
            }
            catch { }
#endif

            metrics.Add(new UsageMetric
            {
                Name = "Claude Limits",
                TotalQuota = 100,
                RemainingQuota = 100, // Fake until the real usage schema is parsed
                ResetTime = DateTime.Now.AddHours(1)
            });
        }
        catch (Exception ex)
        {
#if DEBUG
            try
            {
                var errPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AiMeter", "claude_api_debug.log");
                System.IO.File.AppendAllText(errPath, $"{DateTime.Now:HH:mm:ss.fff} ClaudeWebProvider EXCEPTION: {ex}\n");
            }
            catch { }
#endif
            metrics.Add(new UsageMetric { Name = "Error Fetching", TotalQuota = 100, RemainingQuota = 0 });
        }

        return metrics;
    }
}
