using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiMeter.Models;
using AiMeter.Services;
using Microsoft.Extensions.Logging;

namespace AiMeter.Providers;

/// <summary>
/// Reports opencode.ai "Go" usage, mirroring <see cref="ClaudeWebProvider"/>'s two-step
/// browser-session approach. Until the user logs in, this makes no network call and spins up
/// no WebView2 — it just returns the "Auth Required" placeholder, so registering it is free.
///
/// opencode.ai exposes no JSON usage API; the Go usage page is a SolidStart SSR route whose
/// data is embedded in the page HTML. Two steps (reverse-engineered from a real session):
///   1) GET /go — the (authenticated) Go landing page links to the user's workspace via
///      &lt;a href="/workspace/{workspaceId}/go"&gt;. We scrape that path (mirrors Claude's
///      /api/organizations → org-id lookup) so the workspace id is discovered, not hardcoded.
///   2) GET /workspace/{id}/go — its SolidStart hydration payload carries the usage stats:
///      rollingUsage/weeklyUsage/monthlyUsage, each { status, resetInSec, usagePercent }.
/// </summary>
public class OpenCodeProvider : IProvider
{
    public string Name => "OpenCode";

    // The three Go usage windows, in the order they appear on the dashboard. Keys are the
    // stable field names in opencode.ai's hydration payload; names are what the widget shows.
    private static readonly (string Key, string Name)[] UsageKinds =
    {
        ("rollingUsage", "OpenCode Rolling"),
        ("weeklyUsage",  "OpenCode Weekly"),
        ("monthlyUsage", "OpenCode Monthly"),
    };

    private readonly IOpenCodeSession _session;
    private readonly IOpenCodeApiClient _apiClient;
    private readonly ILogger<OpenCodeProvider> _logger;

    public OpenCodeProvider(IOpenCodeSession session, IOpenCodeApiClient apiClient, ILogger<OpenCodeProvider> logger)
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
            metrics.Add(new UsageMetric { Name = "OpenCode (Auth Required)", TotalQuota = 100, RemainingQuota = 0 });
            return metrics;
        }

        try
        {
            // Step 1: discover the workspace usage URL from the Go landing page.
            var (goStatus, goBody) = await _apiClient.GetAsync("/go");
            if (goStatus is 401 or 403)
            {
                _logger.LogWarning("OpenCode session expired (status {Status}); clearing", goStatus);
                _session.Clear();
                metrics.Add(new UsageMetric { Name = "OpenCode (Session Expired)", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }
            if (goStatus != 200 || goBody is null)
            {
                _logger.LogWarning("OpenCode /go request failed with status {Status}", goStatus);
                metrics.Add(new UsageMetric { Name = "OpenCode (Error Fetching)", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }

            var workspacePath = ExtractWorkspaceGoPath(goBody);
            if (workspacePath is null)
            {
                _logger.LogWarning("OpenCode: no /workspace/{{id}}/go link on the Go page (no workspace yet?).");
                metrics.Add(new UsageMetric { Name = "OpenCode (No Workspace)", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }

            // Step 2: fetch the workspace usage page and parse its embedded stats.
            var (usageStatus, usageBody) = await _apiClient.GetAsync(workspacePath);
            if (usageStatus is 401 or 403)
            {
                _logger.LogWarning("OpenCode session expired on {Path} (status {Status}); clearing", workspacePath, usageStatus);
                _session.Clear();
                metrics.Add(new UsageMetric { Name = "OpenCode (Session Expired)", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }
            if (usageStatus != 200 || usageBody is null)
            {
                _logger.LogWarning("OpenCode usage page {Path} failed with status {Status}", workspacePath, usageStatus);
                metrics.Add(new UsageMetric { Name = "OpenCode (Error Fetching)", TotalQuota = 100, RemainingQuota = 0 });
                return metrics;
            }

            metrics.AddRange(ParseUsage(usageBody));

            if (metrics.Count == 0)
            {
                _logger.LogWarning("OpenCode usage page parsed no stats ({Length} chars).", usageBody.Length);
                metrics.Add(new UsageMetric { Name = "OpenCode (No Usage Data)", TotalQuota = 100, RemainingQuota = 0 });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCode usage fetch threw an unhandled exception");
            metrics.Add(new UsageMetric { Name = "OpenCode (Error Fetching)", TotalQuota = 100, RemainingQuota = 0 });
        }

        return metrics;
    }

    /// <summary>
    /// Pulls the first <c>/workspace/{id}/go</c> link out of the Go landing page HTML — the
    /// authenticated page renders it as the workspace CTA. Returns null if none is present.
    /// </summary>
    private static string? ExtractWorkspaceGoPath(string html)
    {
        var m = Regex.Match(html, "/workspace/wrk_[A-Za-z0-9]+/go");
        return m.Success ? m.Value : null;
    }

    /// <summary>
    /// Extracts the Rolling/Weekly/Monthly usage from the workspace page's SolidStart hydration
    /// payload, where each stat appears as e.g. <c>rollingUsage:$R[31]={status:"ok",
    /// resetInSec:13824,usagePercent:4}</c> (the <c>$R[n]=</c> is seroval's back-reference tag
    /// and may be absent). <c>usagePercent</c> is percent *used*, so remaining = 100 − used
    /// (matching the Claude provider); <c>resetInSec</c> is seconds until the window resets.
    /// </summary>
    private static IEnumerable<UsageMetric> ParseUsage(string html)
    {
        foreach (var (key, name) in UsageKinds)
        {
            // Match "<key>:[$R[n]=]{ ... }" — object body has no nested braces, so [^{}]* is
            // safe. Requiring the "{" skips the unrelated "<key>:null" in other payload objects.
            var obj = Regex.Match(html, Regex.Escape(key) + @":(?:\$R\[\d+\]=)?\{(?<body>[^{}]*)\}");
            if (!obj.Success) continue;
            var body = obj.Groups["body"].Value;

            var pct = Regex.Match(body, @"usagePercent:(?<v>\d+(?:\.\d+)?)");
            if (!pct.Success) continue;
            var percentUsed = double.Parse(pct.Groups["v"].Value, CultureInfo.InvariantCulture);

            DateTime? resetTime = null;
            var sec = Regex.Match(body, @"resetInSec:(?<v>\d+)");
            if (sec.Success && long.TryParse(sec.Groups["v"].Value, out var seconds))
            {
                resetTime = DateTime.Now.AddSeconds(seconds);
            }

            yield return new UsageMetric
            {
                Name = name,
                TotalQuota = 100,
                RemainingQuota = Math.Max(0, 100 - percentUsed),
                ResetTime = resetTime,
            };
        }
    }
}
