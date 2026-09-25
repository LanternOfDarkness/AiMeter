using System;
using System.Collections.Generic;
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
    // Shape of the console's GET /api/go/status (schema from the OpenCode Console bundle):
    // micro-cent amounts are bigint strings; fiveHour has no resetsAt before first use.
    private const string StatusJson = """
        {
          "product": "go",
          "cancelAtPeriodEnd": false,
          "access": {
            "startsAt": "2026-09-10T00:00:00Z",
            "endsAt": "2026-10-10T00:00:00Z",
            "cancelAtPeriodEnd": false,
            "meters": {
              "fiveHour": { "startsAt": "2026-09-25T18:00:00Z", "resetsAt": "2026-09-25T23:00:00Z", "limitMicroCents": "1200000000", "usedMicroCents": "48000000" },
              "week":     { "startsAt": "2026-09-21T00:00:00Z", "resetsAt": "2026-09-28T00:00:00Z", "limitMicroCents": "3000000000", "usedMicroCents": "810000000" },
              "month":    { "limitMicroCents": "6000000000", "usedMicroCents": "780000000" }
            }
          }
        }
        """;

    private const string OrgsJson = """[{"id":"org_personal","name":"Personal"},{"id":"wrk_team","name":"My Workspace"}]""";

    private static OpenCodeProvider Build(FakeOpenCodeSession session, FakeOpenCodeApiClient api) =>
        new(session, api, NullLogger<OpenCodeProvider>.Instance);

    private static CaptureResult Load(params CapturedResponse[] responses) =>
        new(responses, false, OpenCodeProvider.GoPageUrl);

    private static CapturedResponse Status(string? body, string org, int code = 200) =>
        new(OpenCodeProvider.StatusApiPath, code, body, org);

    private static CapturedResponse Orgs(string body = OrgsJson) =>
        new(OpenCodeProvider.OrgsApiPath, 200, body, null);

    [Fact]
    public async Task Logged_out_returns_auth_placeholder_and_makes_no_request()
    {
        var api = new FakeOpenCodeApiClient();
        var provider = Build(new FakeOpenCodeSession { HasSession = false }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Auth Required)");
        api.CaptureCount.Should().Be(0);
    }

    [Fact]
    public async Task Parses_three_usage_meters_from_go_status()
    {
        var api = new FakeOpenCodeApiClient { OnCapture = _ => Load(Status(StatusJson, "wrk_team"), Orgs()) };
        var provider = Build(new FakeOpenCodeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Select(m => m.Name).Should().Equal("OpenCode Rolling", "OpenCode Weekly", "OpenCode Monthly");

        // used / limit → 4%, 27%, 13% used.
        var rolling = metrics[0];
        rolling.RemainingQuota.Should().BeApproximately(96, 0.001);
        rolling.TotalQuota.Should().Be(100);
        rolling.ResetTime.Should().Be(new DateTimeOffset(2026, 9, 25, 23, 0, 0, TimeSpan.Zero).LocalDateTime);
        rolling.WindowDuration.Should().Be(TimeSpan.FromHours(5));

        metrics[1].RemainingQuota.Should().BeApproximately(73, 0.001);

        // Monthly resets with the subscription period.
        var monthly = metrics[2];
        monthly.RemainingQuota.Should().BeApproximately(87, 0.001);
        monthly.ResetTime.Should().Be(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero).LocalDateTime);
        monthly.WindowDuration.Should().Be(TimeSpan.FromDays(30));
        api.CaptureCount.Should().Be(1);
    }

    [Fact]
    public void Unused_rolling_window_has_no_reset_time()
    {
        var json = StatusJson.Replace("\"startsAt\": \"2026-09-25T18:00:00Z\", \"resetsAt\": \"2026-09-25T23:00:00Z\", ", "");

        var metrics = OpenCodeProvider.ParseStatus(json, out var problem);

        problem.Should().BeNull();
        metrics[0].ResetTime.Should().BeNull();
    }

    [Fact]
    public async Task Null_status_switches_to_the_org_that_has_the_subscription()
    {
        // The console defaults to the personal org (null status); the plan lives in wrk_team.
        // It reads the selection from localStorage as a JSON string, so the raw attempt is ignored.
        var api = new FakeOpenCodeApiClient();
        api.OnCapture = a =>
        {
            var selected = a.LocalStorage.TryGetValue(OpenCodeProvider.SelectedOrgStorageKey, out var v) && v == "\"wrk_team\""
                ? "wrk_team" : "org_personal";
            return Load(Status(selected == "wrk_team" ? StatusJson : "null", selected), Orgs());
        };
        var provider = Build(new FakeOpenCodeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Select(m => m.Name).Should().Equal("OpenCode Rolling", "OpenCode Weekly", "OpenCode Monthly");
        api.LocalStorage[OpenCodeProvider.SelectedOrgStorageKey].Should().Be("\"wrk_team\"", "the working org stays selected for later polls");

        var loadsAfterSearch = api.CaptureCount;
        await provider.GetMetricsAsync();
        api.CaptureCount.Should().Be(loadsAfterSearch + 1, "once selected, a poll is a single page load");
    }

    [Fact]
    public async Task No_subscription_in_any_org_restores_selection_and_shows_placeholder()
    {
        var api = new FakeOpenCodeApiClient();
        api.LocalStorage[OpenCodeProvider.SelectedOrgStorageKey] = "\"org_personal\"";
        api.OnCapture = a =>
        {
            var stored = a.LocalStorage.GetValueOrDefault(OpenCodeProvider.SelectedOrgStorageKey);
            var selected = stored?.Contains("wrk_team") == true ? "wrk_team" : "org_personal";
            return Load(Status("null", selected), Orgs());
        };
        var provider = Build(new FakeOpenCodeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (No Subscription)");
        api.LocalStorage[OpenCodeProvider.SelectedOrgStorageKey].Should().Be("\"org_personal\"");

        // Backs off: the next poll doesn't repeat the org search.
        var loads = api.CaptureCount;
        await provider.GetMetricsAsync();
        api.CaptureCount.Should().Be(loads + 1);
    }

    [Fact]
    public async Task No_console_subscription_falls_back_to_legacy_workspace_page()
    {
        // Slice of the old site's workspace page hydration payload (captured from a live session).
        const string legacyHtml =
            "…rollingUsage:$R[31]={status:\"ok\",resetInSec:13824,usagePercent:4}," +
            "weeklyUsage:$R[32]={status:\"ok\",resetInSec:122025,usagePercent:27}," +
            "monthlyUsage:$R[33]={status:\"ok\",resetInSec:2588405,usagePercent:13}…" +
            "…billing:$R[36]={monthlyUsage:null,subscription:null}…";
        var api = new FakeOpenCodeApiClient
        {
            OnCapture = _ => Load(Status("null", "wrk_team"), Orgs("""[{"id":"wrk_team","name":"My Workspace"}]""")),
        };
        api.Pages["https://opencode.ai/workspace/wrk_team/go"] = legacyHtml;
        var provider = Build(new FakeOpenCodeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        api.NavigatedUrls.Should().Equal("https://opencode.ai/workspace/wrk_team/go");
        metrics.Select(m => (m.Name, m.RemainingQuota)).Should().Equal(
            ("OpenCode Rolling", 96d), ("OpenCode Weekly", 73d), ("OpenCode Monthly", 87d));
    }

    [Fact]
    public async Task No_subscription_sets_account_note_and_a_later_plan_clears_it()
    {
        var hasPlan = false;
        var api = new FakeOpenCodeApiClient
        {
            OnCapture = _ => Load(Status(hasPlan ? StatusJson : "null", "wrk_team"), Orgs("""[{"id":"wrk_team","name":"My Workspace"}]""")),
        };
        var session = new FakeOpenCodeSession { HasSession = true };
        var provider = Build(session, api);

        await provider.GetMetricsAsync();
        session.StatusNote.Should().Be(OpenCodeProvider.NoSubscriptionNote);

        hasPlan = true;
        await provider.GetMetricsAsync();
        session.StatusNote.Should().BeNull();
    }

    [Fact]
    public async Task Legacy_page_check_backs_off_until_the_next_login()
    {
        var api = new FakeOpenCodeApiClient
        {
            OnCapture = _ => Load(Status("null", "wrk_team"), Orgs("""[{"id":"wrk_team","name":"My Workspace"}]""")),
        };
        var session = new FakeOpenCodeSession { HasSession = true };
        var provider = Build(session, api);

        await provider.GetMetricsAsync();
        await provider.GetMetricsAsync();
        api.NavigatedUrls.Should().HaveCount(1, "a miss on the old site is not retried every poll");

        session.Store(new[] { ("c", "v") });
        await provider.GetMetricsAsync();
        api.NavigatedUrls.Should().HaveCount(2, "a fresh login retries straight away");
    }

    [Fact]
    public async Task Bounced_to_login_clears_session()
    {
        var session = new FakeOpenCodeSession { HasSession = true };
        var api = new FakeOpenCodeApiClient
        {
            OnCapture = _ => new CaptureResult(Array.Empty<CapturedResponse>(), true, "https://opencode.ai/console/login?next=%2Fconsole%2Fgo"),
        };
        var provider = Build(session, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Session Expired)");
        session.HasSession.Should().BeFalse();
    }

    [Fact]
    public async Task Nothing_captured_surfaces_error_and_keeps_session()
    {
        var session = new FakeOpenCodeSession { HasSession = true };
        var api = new FakeOpenCodeApiClient { OnCapture = _ => Load() };
        var provider = Build(session, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Error Fetching)");
        session.HasSession.Should().BeTrue();
    }
}
