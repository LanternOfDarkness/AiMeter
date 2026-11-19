using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AiMeter.Models;
using AiMeter.Services;
using Microsoft.Extensions.Logging;

namespace AiMeter.Providers;

/// <summary>
/// Reports opencode.ai "Go" usage from the Console's JSON API. The console's HTML routes are
/// now only a client-rendered shell, so the provider reads the same endpoints the console's
/// own "My usage" page calls:
///   GET /console/api/user/workspaces   → [{ id, name, blocked, … }]
///   GET /console/api/go/status?fresh=true, header x-org-id: wrk_…
///                                      → { access: { meters: { fiveHour, week, month } } }
/// Each meter carries limit/used microcents and an ISO reset timestamp.
///
/// Requests run through the logged-in hidden WebView2 (see <see cref="OpenCodeApiClient"/>),
/// so the console's own session cookie and bot protection apply.
/// </summary>
public class OpenCodeProvider : IProvider
{
    public string Name => "OpenCode";

    public const string WorkspacesPath = "/console/api/user/workspaces";
    public const string GoStatusPath = "/console/api/go/status?fresh=true";

    private const string NoWorkspaceName = "OpenCode (No Workspace)";
    private const string NoSubscriptionName = "OpenCode (No Subscription)";
    public const string NoSubscriptionNote = "No active Go subscription";
    public const string ErrorNote = "Usage unavailable (see log)";

    private static readonly (string JsonKey, string Name, TimeSpan Duration)[] Meters =
    {
        ("fiveHour", "OpenCode Rolling", TimeSpan.FromHours(5)),
        ("week",     "OpenCode Weekly",  TimeSpan.FromDays(7)),
        ("month",    "OpenCode Monthly", TimeSpan.FromDays(30)),
    };

    private readonly IOpenCodeSession _session;
    private readonly IOpenCodeApiClient _apiClient;
    private readonly ILogger<OpenCodeProvider> _logger;
    private string? _workspaceId;

    public OpenCodeProvider(IOpenCodeSession session, IOpenCodeApiClient apiClient, ILogger<OpenCodeProvider> logger)
    {
        _session = session;
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<ProviderFetchResult> GetMetricsAsync()
    {
        if (!_session.HasSession)
        {
            return new ProviderFetchResult(new[] { Placeholder("OpenCode (Auth Required)") }, ProviderFetchStatus.NoSession);
        }

        try
        {
            if (_workspaceId is null)
            {
                var (workspaceStatus, workspaceBody) = await _apiClient.GetAsync(WorkspacesPath);
                if (workspaceStatus == 401) return Expired($"{WorkspacesPath} returned 401");
                if (workspaceStatus != 200 || string.IsNullOrWhiteSpace(workspaceBody))
                {
                    _logger.LogWarning("OpenCode: workspace lookup failed (HTTP {Status}, {Length} chars)",
                        workspaceStatus, workspaceBody?.Length ?? 0);
                    return Error(NoWorkspaceName);
                }

                _workspaceId = ParseFirstWorkspaceId(workspaceBody);
                if (_workspaceId is null)
                {
                    _logger.LogWarning("OpenCode: no workspace in {Body}", Shorten(workspaceBody));
                    return Error(NoWorkspaceName);
                }
            }

            var (status, body) = await _apiClient.GetAsync(GoStatusPath, _workspaceId);
            if (status == 401) return Expired($"{GoStatusPath} returned 401");
            if (status != 200 || string.IsNullOrWhiteSpace(body))
            {
                _logger.LogWarning("OpenCode: Go status failed for {Workspace} (HTTP {Status}, {Length} chars)",
                    _workspaceId, status, body?.Length ?? 0);
                _workspaceId = null;
                return Error("OpenCode (Error Fetching)");
            }

            var metrics = ParseGoStatus(body);
            if (metrics.Count > 0)
            {
                _logger.LogInformation("OpenCode usage ({Workspace}): {Meters}", _workspaceId,
                    string.Join(", ", metrics.Select(m => $"{m.Name} {100 - m.RemainingQuota:0.#}% used, resets {m.ResetTime:g}")));
                return Report(metrics, ProviderFetchStatus.Success, null);
            }

            // A 200 response with no meters means the workspace has no active Go subscription.
            _logger.LogInformation("OpenCode: workspace {Workspace} has no Go meters", _workspaceId);
            return Report(new[] { Placeholder(NoSubscriptionName) }, ProviderFetchStatus.NoData, NoSubscriptionNote);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "OpenCode: could not parse the console response");
            return Error("OpenCode (Error Fetching)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCode usage fetch threw an unhandled exception");
            return Error("OpenCode (Error Fetching)");
        }
    }

    private ProviderFetchResult Expired(string reason)
    {
        _logger.LogWarning("OpenCode session expired ({Reason}); clearing", reason);
        _workspaceId = null;
        _session.Clear();
        return new ProviderFetchResult(new[] { Placeholder("OpenCode (Session Expired)") }, ProviderFetchStatus.SessionExpired);
    }

    private ProviderFetchResult Error(string placeholderName)
    {
        _workspaceId = null;
        return Report(new[] { Placeholder(placeholderName) }, ProviderFetchStatus.Error, ErrorNote);
    }

    /// <summary>Publishes the account status note shown in Settings, then wraps the metrics and status.</summary>
    private ProviderFetchResult Report(IReadOnlyList<UsageMetric> metrics, ProviderFetchStatus status, string? note)
    {
        _session.SetStatusNote(note);
        return new ProviderFetchResult(metrics, status);
    }

    /// <summary>
    /// The first non-blocked workspace id from /console/api/user/workspaces. The endpoint also
    /// lists team workspaces the user can switch to; arrival order is the console's own.
    /// </summary>
    public static string? ParseFirstWorkspaceId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        string? fallback = null;
        foreach (var workspace in doc.RootElement.EnumerateArray())
        {
            if (!workspace.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;

            var value = id.GetString();
            if (string.IsNullOrWhiteSpace(value)) continue;
            fallback ??= value;

            if (!workspace.TryGetProperty("blocked", out var blocked) || blocked.ValueKind != JsonValueKind.True)
            {
                return value;
            }
        }
        return fallback;
    }

    /// <summary>
    /// Maps the Go status payload's fiveHour/week/month meters to widget metrics. Limits and
    /// usage are microcent strings; a meter with a missing/zero limit is skipped. Reset times
    /// are ISO-8601 timestamps in UTC and are exposed as local <see cref="UsageMetric.ResetTime"/>.
    /// </summary>
    public static List<UsageMetric> ParseGoStatus(string json)
    {
        var metrics = new List<UsageMetric>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("access", out var access)
            || access.ValueKind != JsonValueKind.Object
            || !access.TryGetProperty("meters", out var meters)
            || meters.ValueKind != JsonValueKind.Object)
        {
            return metrics;
        }

        foreach (var (key, name, duration) in Meters)
        {
            if (!meters.TryGetProperty(key, out var meter) || meter.ValueKind != JsonValueKind.Object) continue;
            if (!TryReadNumber(meter, "limitMicroCents", out var limit) || limit <= 0) continue;
            if (!TryReadNumber(meter, "usedMicroCents", out var used)) continue;

            var remaining = Math.Clamp(100d * (1 - used / limit), 0, 100);
            DateTime? resetTime = null;
            if (meter.TryGetProperty("resetsAt", out var resets)
                && resets.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(resets.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var reset))
            {
                resetTime = reset.LocalDateTime;
            }

            metrics.Add(new UsageMetric
            {
                Name = name,
                TotalQuota = 100,
                RemainingQuota = remaining,
                ResetTime = resetTime,
                WindowDuration = duration,
            });
        }
        return metrics;
    }

    private static bool TryReadNumber(JsonElement parent, string property, out double value)
    {
        value = 0;
        if (!parent.TryGetProperty(property, out var element)) return false;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(element.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static string Shorten(string body) => body.Length <= 300 ? body : body[..300] + "…";

    private static UsageMetric Placeholder(string name) => new() { Name = name, TotalQuota = 100, RemainingQuota = 0 };
}
