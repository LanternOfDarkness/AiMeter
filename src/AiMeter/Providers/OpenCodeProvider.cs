using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiMeter.Models;
using AiMeter.Services;
using Microsoft.Extensions.Logging;

namespace AiMeter.Providers;

/// <summary>
/// Reports opencode.ai "Go" usage. Until the user logs in, this makes no network call and
/// spins up no WebView2 — it just returns the "Auth Required" placeholder, so registering it
/// is free.
///
/// The Go usage lives in the OpenCode Console SPA (opencode.ai/console/go), which loads it
/// from GET /api/go/status using a bearer token and org header the SPA manages itself. Rather
/// than re-implementing that auth, the provider loads the Go page in the logged-in hidden
/// browser and captures the page's own /api/go/status response. Its shape (from the console
/// bundle's schema):
///   { ..., access: null | { startsAt, endsAt, meters: {
///       fiveHour: { startsAt?, resetsAt?, limitMicroCents, usedMicroCents },
///       week:     { startsAt,  resetsAt,  limitMicroCents, usedMicroCents },
///       month:    { limitMicroCents, usedMicroCents } } } }
/// access is null without an active Go subscription; the month meter resets at access.endsAt.
/// </summary>
public class OpenCodeProvider : IProvider
{
    public string Name => "OpenCode";

    public const string GoPageUrl = "https://opencode.ai/console/go";
    public const string StatusApiPath = "/api/go/status";
    public const string OrgsApiPath = "/api/orgs";
    public const string SelectedOrgStorageKey = "opencode-console.org-id";
    private static readonly string[] CapturedApis = { StatusApiPath, OrgsApiPath };
    private const int MaxOrgsToTry = 5;
    private static readonly TimeSpan OrgSearchBackoff = TimeSpan.FromMinutes(30);

    private const string NoSubscriptionName = "OpenCode (No Subscription)";
    public const string NoSubscriptionNote = "No active Go subscription";
    public const string ErrorNote = "Usage unavailable (see log)";

    // Meter keys in the status payload → widget names (unchanged from the old page-scraping
    // provider so users' saved selections/labels/colors keep applying).
    private static readonly (string Key, string Name, TimeSpan? Duration)[] Meters =
    {
        ("fiveHour", "OpenCode Rolling", TimeSpan.FromHours(5)),
        ("week",     "OpenCode Weekly",  TimeSpan.FromDays(7)),
        ("month",    "OpenCode Monthly", null), // taken from the subscription period
    };

    // Same windows as keyed in the old site's workspace-page hydration payload.
    private static readonly (string Key, string Name, TimeSpan Duration)[] LegacyUsageKinds =
    {
        ("rollingUsage", "OpenCode Rolling", TimeSpan.FromHours(5)),
        ("weeklyUsage",  "OpenCode Weekly",  TimeSpan.FromDays(7)),
        ("monthlyUsage", "OpenCode Monthly", TimeSpan.FromDays(30)),
    };

    private readonly IOpenCodeSession _session;
    private readonly IOpenCodeApiClient _apiClient;
    private readonly ILogger<OpenCodeProvider> _logger;
    private DateTime _nextOrgSearch = DateTime.MinValue;
    private DateTime _nextLegacyTry = DateTime.MinValue;
    private int _backoffGeneration;

    public OpenCodeProvider(IOpenCodeSession session, IOpenCodeApiClient apiClient, ILogger<OpenCodeProvider> logger)
    {
        _session = session;
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UsageMetric>> GetMetricsAsync()
    {
        if (!_session.HasSession)
        {
            return new[] { Placeholder("OpenCode (Auth Required)") };
        }

        // A fresh login may have fixed whatever the back-offs were waiting out.
        if (_backoffGeneration != _session.LoginGeneration)
        {
            _backoffGeneration = _session.LoginGeneration;
            _nextOrgSearch = _nextLegacyTry = DateTime.MinValue;
        }

        try
        {
            var load = await _apiClient.CaptureResponsesAsync(GoPageUrl, CapturedApis);
            if (IsLoggedOut(load))
            {
                _logger.LogWarning("OpenCode session expired (page {Url}); clearing", load.FinalUrl);
                _session.Clear();
                return new[] { Placeholder("OpenCode (Session Expired)") };
            }

            var status = BestStatus(load);
            if (status is null)
            {
                _logger.LogWarning("OpenCode {Api} not captured (page {Url}, captured: {Captured})",
                    StatusApiPath, load.FinalUrl, Describe(load));
                return Report(new[] { Placeholder("OpenCode (Error Fetching)") }, ErrorNote);
            }

            // A JSON null status means the console's selected org has no Go subscription -
            // typically the auto-created personal org rather than the (legacy wrk_) workspace
            // that holds the plan. Look for the plan in the user's other orgs.
            if (IsNullBody(status.Body) && DateTime.Now >= _nextOrgSearch)
            {
                var found = await FindOrgWithSubscriptionAsync(load, status.OrgId);
                if (found is not null)
                {
                    status = found;
                }
                else
                {
                    _nextOrgSearch = DateTime.Now + OrgSearchBackoff;
                }
            }

            // Still no plan in the console: Go subscriptions made on the old site can still live
            // only on its per-workspace page (/workspace/wrk_…/go). The console hands us the
            // workspace id (legacy workspaces became wrk_ orgs), which /go no longer links to.
            // Each miss can cost a 20s wait on the old login page, hence the back-off.
            if (IsNullBody(status.Body) && DateTime.Now >= _nextLegacyTry)
            {
                var legacy = await GetLegacyWorkspaceUsageAsync(load, status.OrgId);
                if (legacy.Count > 0)
                {
                    return Report(legacy, null);
                }
                _nextLegacyTry = DateTime.Now + OrgSearchBackoff;
            }

            var metrics = ParseStatus(status.Body!, out var problem);
            var note = metrics.Any(m => m.Name == NoSubscriptionName) ? NoSubscriptionNote
                : problem is not null ? ErrorNote
                : null;
            // Log a problem once when it appears rather than on every poll.
            if (problem is not null && note != _session.StatusNote)
            {
                _logger.LogWarning("OpenCode {Api} (org {Org}): {Problem}", StatusApiPath, status.OrgId ?? "default", problem);
            }
            return Report(metrics, note);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCode usage fetch threw an unhandled exception");
            return Report(new[] { Placeholder("OpenCode (Error Fetching)") }, ErrorNote);
        }
    }

    /// <summary>Publishes the account status note shown in Settings, then passes the metrics through.</summary>
    private IReadOnlyList<UsageMetric> Report(IReadOnlyList<UsageMetric> metrics, string? note)
    {
        _session.SetStatusNote(note);
        return metrics;
    }

    /// <summary>
    /// Switches the console's selected org (its own localStorage key) to each of the user's
    /// other orgs and reloads, returning the first non-null status. The winning org stays
    /// selected, so later polls hit it directly; if none has a plan the original is restored.
    /// </summary>
    private async Task<CapturedResponse?> FindOrgWithSubscriptionAsync(CaptureResult load, string? currentOrgId)
    {
        var orgs = ParseOrgs(load.Responses.LastOrDefault(r => r.Path == OrgsApiPath && r.StatusCode == 200)?.Body);
        var candidates = orgs.Where(o => o.Id != currentOrgId).Take(MaxOrgsToTry).ToList();
        if (candidates.Count == 0)
        {
            _logger.LogWarning("OpenCode: selected org {Org} has no Go subscription and no other orgs were found ({Count} total)",
                currentOrgId ?? "default", orgs.Count);
            return null;
        }

        var original = await _apiClient.GetLocalStorageAsync(SelectedOrgStorageKey);
        foreach (var (id, name) in candidates)
        {
            // The console's storage encoding isn't documented: try the raw id, then a JSON
            // string, and trust whichever the page actually sends back as x-org-id.
            foreach (var stored in new[] { id, JsonSerializer.Serialize(id) })
            {
                await _apiClient.SetLocalStorageAsync(SelectedOrgStorageKey, stored);
                var retry = await _apiClient.CaptureResponsesAsync(GoPageUrl, CapturedApis);
                var status = BestStatus(retry);
                if (status is null || status.OrgId != id) continue;

                if (!IsNullBody(status.Body))
                {
                    _logger.LogInformation("OpenCode: Go subscription found in org '{Name}' ({Org}); selected it", name, id);
                    return status;
                }
                break; // right org, no plan here
            }
        }

        await _apiClient.SetLocalStorageAsync(SelectedOrgStorageKey, original);
        _logger.LogWarning("OpenCode: no Go subscription in any of {Count} orgs ({Names})",
            orgs.Count, string.Join(", ", orgs.Select(o => o.Name)));
        return null;
    }

    /// <summary>
    /// Reads Rolling/Weekly/Monthly usage from the old site's workspace page for each of the
    /// user's legacy (wrk_) workspaces; empty if none has usage there.
    /// </summary>
    private async Task<List<UsageMetric>> GetLegacyWorkspaceUsageAsync(CaptureResult load, string? currentOrgId)
    {
        var workspaceIds = ParseOrgs(load.Responses.LastOrDefault(r => r.Path == OrgsApiPath && r.StatusCode == 200)?.Body)
            .Select(o => o.Id)
            .Prepend(currentOrgId)
            .Where(id => id is not null && id.StartsWith("wrk_", StringComparison.Ordinal))
            .Distinct()
            .Take(MaxOrgsToTry)
            .ToList();

        foreach (var id in workspaceIds)
        {
            // Navigate rather than fetch(): the page may bounce through auth.opencode.ai, which a
            // same-origin fetch can't follow.
            var (finalUrl, html) = await _apiClient.NavigateAndReadAsync($"https://opencode.ai/workspace/{id}/go");
            var metrics = html is not null ? ParseLegacyUsage(html).ToList() : new List<UsageMetric>();
            if (metrics.Count > 0)
            {
                return metrics;
            }
            _logger.LogWarning("OpenCode legacy workspace page for {Org}: ended at {Url}, {Length} chars, no usage stats",
                id, finalUrl, html?.Length ?? 0);
        }
        return new List<UsageMetric>();
    }

    /// <summary>
    /// Extracts usage from the old workspace page's SolidStart hydration payload, where each
    /// stat appears as e.g. <c>rollingUsage:$R[31]={status:"ok",resetInSec:13824,usagePercent:4}</c>
    /// (the <c>$R[n]=</c> back-reference tag may be absent). usagePercent is percent used.
    /// </summary>
    public static IEnumerable<UsageMetric> ParseLegacyUsage(string html)
    {
        foreach (var (key, name, duration) in LegacyUsageKinds)
        {
            // Object body has no nested braces, so [^{}]* is safe. Requiring the "{" skips the
            // unrelated "<key>:null" in other payload objects.
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
                WindowDuration = duration,
            };
        }
    }

    private static bool IsLoggedOut(CaptureResult load) =>
        load.BouncedToLogin || load.Responses.Any(r => r.Path == StatusApiPath && r.StatusCode is 401 or 403);

    // The page can query status more than once (e.g. before and after the org resolves):
    // prefer the last one that carries a subscription, else the last successful one.
    private static CapturedResponse? BestStatus(CaptureResult load)
    {
        var ok = load.Responses.Where(r => r.Path == StatusApiPath && r.StatusCode == 200 && r.Body is not null).ToList();
        return ok.LastOrDefault(r => !IsNullBody(r.Body)) ?? ok.LastOrDefault();
    }

    private static bool IsNullBody(string? body) => body is null || body.Trim() == "null";

    private static string Describe(CaptureResult load) =>
        load.Responses.Count == 0 ? "nothing" : string.Join(", ", load.Responses.Select(r => $"{r.Path}={r.StatusCode}"));

    /// <summary>Parses GET /api/orgs: an array of { id, name }.</summary>
    public static List<(string Id, string Name)> ParseOrgs(string? json)
    {
        var orgs = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(json)) return orgs;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return orgs;
            foreach (var org in doc.RootElement.EnumerateArray())
            {
                if (org.ValueKind == JsonValueKind.Object
                    && org.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var name = org.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : id.GetString()!;
                    orgs.Add((id.GetString()!, name));
                }
            }
        }
        catch (JsonException)
        {
        }
        return orgs;
    }

    /// <summary>
    /// Turns a /api/go/status payload into the Rolling/Weekly/Monthly metrics. Percent used is
    /// usedMicroCents / limitMicroCents (what the console's own meters show); remaining =
    /// 100 − used. Returns a single placeholder and sets <paramref name="problem"/> when the
    /// payload has no usable meters.
    /// </summary>
    public static List<UsageMetric> ParseStatus(string json, out string? problem)
    {
        problem = null;
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            problem = $"response is not JSON ({json.Length} chars)";
            return new List<UsageMetric> { Placeholder("OpenCode (Error Fetching)") };
        }

        if (root.ValueKind == JsonValueKind.Null)
        {
            problem = "no Go subscription in this org (status is null)";
            return new List<UsageMetric> { Placeholder(NoSubscriptionName) };
        }
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("access", out var access))
        {
            problem = "no 'access' field; top-level keys: " + TopLevelKeys(root);
            return new List<UsageMetric> { Placeholder("OpenCode (No Usage Data)") };
        }
        if (access.ValueKind != JsonValueKind.Object)
        {
            problem = "no active Go subscription (access is null)";
            return new List<UsageMetric> { Placeholder(NoSubscriptionName) };
        }
        if (!access.TryGetProperty("meters", out var meters) || meters.ValueKind != JsonValueKind.Object)
        {
            problem = "no 'meters' in access; keys: " + TopLevelKeys(access);
            return new List<UsageMetric> { Placeholder("OpenCode (No Usage Data)") };
        }

        var periodStart = ReadDate(access, "startsAt");
        var periodEnd = ReadDate(access, "endsAt");

        var metrics = new List<UsageMetric>();
        foreach (var (key, name, duration) in Meters)
        {
            if (!meters.TryGetProperty(key, out var meter) || meter.ValueKind != JsonValueKind.Object) continue;

            var used = ReadNumber(meter, "usedMicroCents");
            var limit = ReadNumber(meter, "limitMicroCents");
            if (used is null || limit is null) continue;

            var percentUsed = limit > 0 ? Math.Clamp(used.Value / limit.Value * 100, 0, 100) : 0;
            var isMonth = duration is null;

            metrics.Add(new UsageMetric
            {
                Name = name,
                TotalQuota = 100,
                RemainingQuota = 100 - percentUsed,
                // fiveHour has no resetsAt until the first request opens a window.
                ResetTime = isMonth ? periodEnd : ReadDate(meter, "resetsAt"),
                WindowDuration = isMonth
                    ? (periodStart is { } s && periodEnd is { } e && e > s ? e - s : TimeSpan.FromDays(30))
                    : duration,
            });
        }

        if (metrics.Count == 0)
        {
            problem = "meters present but none parseable; keys: " + TopLevelKeys(meters);
            metrics.Add(Placeholder("OpenCode (No Usage Data)"));
        }
        return metrics;
    }

    private static UsageMetric Placeholder(string name) => new() { Name = name, TotalQuota = 100, RemainingQuota = 0 };

    private static string TopLevelKeys(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object ? string.Join(", ", e.EnumerateObject().Select(p => p.Name)) : e.ValueKind.ToString();

    // Micro-cent amounts are bigints encoded as JSON strings; accept plain numbers too.
    private static double? ReadNumber(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    private static DateTime? ReadDate(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            return dto.LocalDateTime;
        }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var epoch))
        {
            // Epoch millis vs seconds.
            var instant = epoch > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(epoch) : DateTimeOffset.FromUnixTimeSeconds(epoch);
            return instant.LocalDateTime;
        }
        return null;
    }
}
