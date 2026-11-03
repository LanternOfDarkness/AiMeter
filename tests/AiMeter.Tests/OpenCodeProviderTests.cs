using System;
using System.Linq;
using System.Threading.Tasks;
using AiMeter.Providers;
using AiMeter.Services;
using AiMeter.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiMeter.Tests;

public class OpenCodeProviderTests
{
    private const string Workspace = "wrk_01KX5JXZQB94WAJNBNGER9A12C";
    private static readonly string UsageUrl = OpenCodeProvider.UsagePageUrl(Workspace);

    // Go card from a live console "My usage" page (classes trimmed).
    private const string UsageHtml = """
        <nav><a href="/console/workspace" class="nav-avatar-link"></a><button title="Switch workspace: Default"></button>
        <a href="/console/wrk_01KX5JXZQB94WAJNBNGER9A12C/activity" class="active" aria-current="page"><span>Activity</span></a></nav>
        <section class="flex" aria-label="Go usage"><div><h2><span>Go</span><span class="truncate">Renews Nov 16</span></h2></div>
        <div class="grid"><div class="min-w-0"><p class="text-foreground">Rolling</p><p class="truncate" title="11/3/2026, 3:22:58 AM">Resets in 0h 35m</p></div>
        <div class="flex h-2 w-full gap-1" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-label="Rolling Go usage" aria-valuenow="100" aria-valuetext="100% used"><div data-slot="progress-fill" style="flex: 100 1 0%;"></div></div>
        <p class="text-right">100% used</p></div>
        <div class="grid"><div class="min-w-0"><p class="text-foreground">Weekly</p><p class="truncate" title="11/9/2026, 2:00:00 AM">Resets in 5d 23h</p></div>
        <div class="flex h-2 w-full gap-1" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-label="Weekly Go usage" aria-valuenow="46" aria-valuetext="46% used"><div data-slot="progress-fill"></div><div data-slot="progress-remainder"></div></div>
        <p class="text-right">46% used</p></div>
        <div class="grid"><div class="min-w-0"><p class="text-foreground">Monthly</p><p class="truncate" title="11/16/2026, 8:37:04 PM">Resets in 13d 17h</p></div>
        <div class="flex h-2 w-full gap-1" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-label="Monthly Go usage" aria-valuenow="62" aria-valuetext="62% used"><div data-slot="progress-fill"></div></div>
        <p class="text-right">62% used</p></div></section>
        <section class="flex" aria-label="Credits"><h2><span>Credits</span></h2></section>
        """;

    private const string CreditsOnlyHtml = """<section class="flex" aria-label="Credits"><h2><span>Credits</span></h2></section>""";

    // The page was captured at 02:47:xx local time on Nov 3.
    private static readonly DateTime CapturedAt = new(2026, 11, 3, 2, 47, 0);

    private static OpenCodeProvider Build(FakeOpenCodeSession session, FakeOpenCodeApiClient api) =>
        new(session, api, NullLogger<OpenCodeProvider>.Instance);

    private static FakeOpenCodeApiClient ApiWithUsage(string html, bool ready = true)
    {
        var api = new FakeOpenCodeApiClient();
        api.Pages[OpenCodeProvider.ConsoleUrl] = new RenderedPage($"https://opencode.ai/console/{Workspace}/activity", "<html></html>", true, false);
        api.Pages[UsageUrl] = new RenderedPage(UsageUrl, html, ready, false);
        return api;
    }

    [Fact]
    public async Task Logged_out_returns_auth_placeholder_and_makes_no_request()
    {
        var api = new FakeOpenCodeApiClient();
        var provider = Build(new FakeOpenCodeSession { HasSession = false }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Auth Required)");
        api.NavigatedUrls.Should().BeEmpty();
    }

    [Fact]
    public void Parses_three_meters_from_usage_page()
    {
        var metrics = OpenCodeProvider.ParseUsagePage(UsageHtml, CapturedAt);

        metrics.Select(m => (m.Name, m.RemainingQuota, m.WindowDuration)).Should().Equal(
            ("OpenCode Rolling", 0d, TimeSpan.FromHours(5)),
            ("OpenCode Weekly", 54d, TimeSpan.FromDays(7)),
            ("OpenCode Monthly", 38d, TimeSpan.FromDays(30)));
        metrics.Should().OnlyContain(m => m.TotalQuota == 100);
        metrics[0].ResetTime.Should().Be(new DateTime(2026, 11, 3, 3, 22, 58));
        metrics[1].ResetTime.Should().Be(new DateTime(2026, 11, 9, 2, 0, 0));
        metrics[2].ResetTime.Should().Be(new DateTime(2026, 11, 16, 20, 37, 4));
    }

    [Fact]
    public void Unparseable_reset_title_falls_back_to_relative_text()
    {
        var html = UsageHtml.Replace("title=\"11/3/2026, 3:22:58 AM\"", "title=\"soon\"");

        var metrics = OpenCodeProvider.ParseUsagePage(html, CapturedAt);

        metrics[0].ResetTime.Should().Be(CapturedAt.AddMinutes(35));
    }

    [Fact]
    public void Meter_without_reset_line_has_no_reset_time()
    {
        var html = UsageHtml.Replace("<p class=\"truncate\" title=\"11/3/2026, 3:22:58 AM\">Resets in 0h 35m</p>", "");

        var metrics = OpenCodeProvider.ParseUsagePage(html, CapturedAt);

        metrics[0].ResetTime.Should().BeNull();
    }

    [Theory]
    [InlineData("https://opencode.ai/console/wrk_ABC123/activity", null, "wrk_ABC123")]
    [InlineData("https://opencode.ai/console", "<a href=\"/console/workspace\"></a><a href=\"/console/wrk_XYZ/go\"></a>", "wrk_XYZ")]
    [InlineData("https://opencode.ai/console/workspace", "<a href=\"/console/workspace\"></a>", null)]
    public void Parses_workspace_id(string url, string? html, string? expected)
    {
        OpenCodeProvider.ParseWorkspaceId(url, html).Should().Be(expected);
    }

    [Fact]
    public async Task Finds_workspace_once_then_reads_its_usage_page()
    {
        var api = ApiWithUsage(UsageHtml);
        var session = new FakeOpenCodeSession { HasSession = true };
        var provider = Build(session, api);

        var metrics = await provider.GetMetricsAsync();
        await provider.GetMetricsAsync();

        metrics.Select(m => m.Name).Should().Equal("OpenCode Rolling", "OpenCode Weekly", "OpenCode Monthly");
        api.NavigatedUrls.Should().Equal(OpenCodeProvider.ConsoleUrl, UsageUrl, UsageUrl);
        session.StatusNote.Should().BeNull();
    }

    [Fact]
    public async Task Rendered_page_without_go_card_means_no_subscription()
    {
        var api = ApiWithUsage(CreditsOnlyHtml);
        var session = new FakeOpenCodeSession { HasSession = true };
        var provider = Build(session, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (No Subscription)");
        session.StatusNote.Should().Be(OpenCodeProvider.NoSubscriptionNote);

        api.Pages[UsageUrl] = new RenderedPage(UsageUrl, UsageHtml, true, false);
        await provider.GetMetricsAsync();
        session.StatusNote.Should().BeNull();
    }

    [Fact]
    public async Task Go_card_still_loading_is_an_error_not_no_subscription()
    {
        // Skeleton Go card (no progress bars yet) beside a filled Credits card.
        const string loading = """<section aria-label="Go usage"><div aria-busy="true"><p>Rolling</p><div class="h-2"></div></div></section>""" + CreditsOnlyHtml;
        var session = new FakeOpenCodeSession { HasSession = true };
        var provider = Build(session, ApiWithUsage(loading));

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Error Fetching)");
        session.StatusNote.Should().Be(OpenCodeProvider.ErrorNote);
    }

    [Fact]
    public async Task Bounced_to_login_clears_session()
    {
        var session = new FakeOpenCodeSession { HasSession = true };
        var api = new FakeOpenCodeApiClient();
        api.Pages[OpenCodeProvider.ConsoleUrl] = new RenderedPage("https://opencode.ai/console/login", null, false, true);
        var provider = Build(session, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Session Expired)");
        session.HasSession.Should().BeFalse();
    }

    [Fact]
    public async Task Page_that_never_renders_surfaces_error_keeps_session_and_rediscovers_workspace()
    {
        var session = new FakeOpenCodeSession { HasSession = true };
        var api = ApiWithUsage("<html></html>", ready: false);
        var provider = Build(session, api);

        var metrics = await provider.GetMetricsAsync();
        await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Error Fetching)");
        session.HasSession.Should().BeTrue();
        session.StatusNote.Should().Be(OpenCodeProvider.ErrorNote);
        api.NavigatedUrls.Should().Equal(OpenCodeProvider.ConsoleUrl, UsageUrl, OpenCodeProvider.ConsoleUrl, UsageUrl);
    }
}
