using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
/// The Go usage lives on the OpenCode Console's per-workspace "My usage" page
/// (opencode.ai/console/{workspace}/activity/overview). The console no longer exposes a
/// stable JSON endpoint for it, so the provider loads that page in the logged-in hidden
/// browser and reads the meters it renders. Each is an accessible progress bar:
///   &lt;p title="11/3/2026, 3:22:58 AM"&gt;Resets in 0h 35m&lt;/p&gt;
///   &lt;div role="progressbar" aria-label="Rolling Go usage" aria-valuenow="100" …&gt;
/// The workspace id comes from where /console redirects to (or links from it).
/// </summary>
public class OpenCodeProvider : IProvider
{
    public string Name => "OpenCode";

    public const string ConsoleUrl = "https://opencode.ai/console";
    public static string UsagePageUrl(string workspaceId) => $"{ConsoleUrl}/{workspaceId}/activity/overview";

    // Ready once the Go meters render. The Go card first renders as an aria-busy skeleton (no
    // progress bars) next to an already-filled Credits card, so Credits alone only counts as
    // ready when there is no Go card at all (no subscription) and nothing is still loading.
    private const string UsageReadyCheck =
        """document.querySelector('[role="progressbar"][aria-label$="Go usage"]') || (document.querySelector('section[aria-label="Credits"]') && !document.querySelector('section[aria-label="Go usage"], [aria-busy="true"]'))""";
    private const string GoCardMarker = "aria-label=\"Go usage\"";
    private const string WorkspaceReadyCheck =
        """/\/console\/[a-z]+_[A-Za-z0-9]+/.test(location.pathname) || document.querySelector('a[href*="/activity"]')""";

    private const string NoSubscriptionName = "OpenCode (No Subscription)";
    public const string NoSubscriptionNote = "No active Go subscription";
    public const string ErrorNote = "Usage unavailable (see log)";

    // Meter labels on the page → widget names (unchanged so users' saved selections/labels/
    // colors keep applying).
    private static readonly (string Label, string Name, TimeSpan Duration)[] Meters =
    {
        ("Rolling", "OpenCode Rolling", TimeSpan.FromHours(5)),
        ("Weekly",  "OpenCode Weekly",  TimeSpan.FromDays(7)),
        ("Monthly", "OpenCode Monthly", TimeSpan.FromDays(30)),
    };

    private static readonly Regex WorkspaceIdPattern = new(@"/console/(?<id>[a-z]+_[A-Za-z0-9]+)(?:[/?#""]|$)");
    private static readonly Regex ProgressBarTag = new(@"<div\b[^>]*\brole=""progressbar""[^>]*>");
    private static readonly Regex ResetLine = new(@"<p\b[^>]*\btitle=""(?<title>[^""]*)""[^>]*>(?<text>[^<]*)</p>");

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

    public async Task<IReadOnlyList<UsageMetric>> GetMetricsAsync()
    {
        if (!_session.HasSession)
        {
            return new[] { Placeholder("OpenCode (Auth Required)") };
        }

        try
        {
            if (_workspaceId is null)
            {
                var console = await _apiClient.ReadRenderedPageAsync(ConsoleUrl, WorkspaceReadyCheck);
                if (console.BouncedToLogin) return Expired(console.FinalUrl);

                _workspaceId = ParseWorkspaceId(console.FinalUrl, console.Html);
                if (_workspaceId is null)
                {
                    _logger.LogWarning("OpenCode: no workspace found (page {Url}, {Length} chars)", console.FinalUrl, console.Html?.Length ?? 0);
                    return Report(new[] { Placeholder("OpenCode (Error Fetching)") }, ErrorNote);
                }
            }

            var page = await _apiClient.ReadRenderedPageAsync(UsagePageUrl(_workspaceId), UsageReadyCheck);
            if (page.BouncedToLogin) return Expired(page.FinalUrl);

            var metrics = page.Html is not null ? ParseUsagePage(page.Html) : new List<UsageMetric>();
            if (metrics.Count > 0)
            {
                _logger.LogInformation("OpenCode usage ({Ws}): {Meters}", _workspaceId,
                    string.Join(", ", metrics.Select(m => $"{m.Name} {100 - m.RemainingQuota:0.#}% used, resets {m.ResetTime:g}")));
                return Report(metrics, null);
            }
            if (page.Ready && page.Html?.Contains(GoCardMarker, StringComparison.Ordinal) == false)
            {
                // The page rendered (Credits card) but has no Go card.
                return Report(new[] { Placeholder(NoSubscriptionName) }, NoSubscriptionNote);
            }

            // Log once when the problem appears rather than on every poll, and re-discover the
            // workspace next time in case it moved.
            if (_session.StatusNote != ErrorNote)
            {
                _logger.LogWarning("OpenCode: no Go usage on {Url} (ended at {Final}, ready {Ready}, {Length} chars, Go card {GoCard})",
                    UsagePageUrl(_workspaceId), page.FinalUrl, page.Ready, page.Html?.Length ?? 0,
                    page.Html?.Contains(GoCardMarker, StringComparison.Ordinal) == true);
            }
            _workspaceId = null;
            return Report(new[] { Placeholder("OpenCode (Error Fetching)") }, ErrorNote);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCode usage fetch threw an unhandled exception");
            return Report(new[] { Placeholder("OpenCode (Error Fetching)") }, ErrorNote);
        }
    }

    private IReadOnlyList<UsageMetric> Expired(string url)
    {
        _logger.LogWarning("OpenCode session expired (page {Url}); clearing", url);
        _workspaceId = null;
        _session.Clear();
        return new[] { Placeholder("OpenCode (Session Expired)") };
    }

    /// <summary>Publishes the account status note shown in Settings, then passes the metrics through.</summary>
    private IReadOnlyList<UsageMetric> Report(IReadOnlyList<UsageMetric> metrics, string? note)
    {
        _session.SetStatusNote(note);
        return metrics;
    }

    /// <summary>The console workspace id (e.g. wrk_…), from the URL /console settled on, else its first workspace link.</summary>
    public static string? ParseWorkspaceId(string url, string? html)
    {
        var match = WorkspaceIdPattern.Match(url);
        if (!match.Success && html is not null) match = WorkspaceIdPattern.Match(html);
        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>
    /// Reads the Rolling/Weekly/Monthly meters from the rendered "My usage" page. aria-valuenow
    /// is percent used; the reset time is the absolute title of the "Resets in …" line just
    /// before each bar, falling back to the relative text when the title won't parse.
    /// </summary>
    public static List<UsageMetric> ParseUsagePage(string html) => ParseUsagePage(html, DateTime.Now);

    public static List<UsageMetric> ParseUsagePage(string html, DateTime now)
    {
        var metrics = new List<UsageMetric>();
        // Start at the Go card so a meter without a reset line can't borrow an unrelated title.
        var rowStart = Math.Max(0, html.IndexOf(GoCardMarker, StringComparison.Ordinal));
        foreach (Match bar in ProgressBarTag.Matches(html))
        {
            var label = Attribute(bar.Value, "aria-label");
            var meter = Meters.FirstOrDefault(m => label == $"{m.Label} Go usage");
            var before = html[rowStart..bar.Index];
            rowStart = bar.Index + bar.Length;
            if (meter.Name is null || metrics.Any(m => m.Name == meter.Name)) continue;
            if (!double.TryParse(Attribute(bar.Value, "aria-valuenow"), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentUsed)) continue;
            percentUsed = Math.Clamp(percentUsed, 0, 100);

            var reset = ResetLine.Matches(before).LastOrDefault();
            metrics.Add(new UsageMetric
            {
                Name = meter.Name,
                TotalQuota = 100,
                RemainingQuota = 100 - percentUsed,
                ResetTime = reset is null ? null
                    : ParseResetTime(WebUtility.HtmlDecode(reset.Groups["title"].Value), WebUtility.HtmlDecode(reset.Groups["text"].Value), now),
                WindowDuration = meter.Duration,
            });
        }
        return metrics;
    }

    private static string? Attribute(string tag, string name)
    {
        var m = Regex.Match(tag, $@"\b{Regex.Escape(name)}=""(?<v>[^""]*)""");
        return m.Success ? WebUtility.HtmlDecode(m.Groups["v"].Value) : null;
    }

    // The title is the browser's toLocaleString(), whose day/month order depends on the
    // WebView's locale; the relative "Resets in 5d 23h" text settles which reading is right.
    private static DateTime? ParseResetTime(string title, string text, DateTime now)
    {
        DateTime? relative = null;
        var parts = Regex.Matches(text, @"(?<n>\d+)\s*(?<u>[dhms])\b");
        if (parts.Count > 0)
        {
            var span = TimeSpan.Zero;
            foreach (Match p in parts)
            {
                var n = int.Parse(p.Groups["n"].Value, CultureInfo.InvariantCulture);
                span += p.Groups["u"].Value switch
                {
                    "d" => TimeSpan.FromDays(n),
                    "h" => TimeSpan.FromHours(n),
                    "m" => TimeSpan.FromMinutes(n),
                    _ => TimeSpan.FromSeconds(n),
                };
            }
            relative = now + span;
        }

        foreach (var culture in new[] { CultureInfo.CurrentCulture, CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("en-GB") })
        {
            if (DateTime.TryParse(title, culture, DateTimeStyles.AssumeLocal, out var absolute)
                && (relative is null || Math.Abs((absolute - relative.Value).TotalHours) < 1.5))
            {
                return absolute;
            }
        }
        return relative;
    }

    private static UsageMetric Placeholder(string name) => new() { Name = name, TotalQuota = 100, RemainingQuota = 0 };
}
