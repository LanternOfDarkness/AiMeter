using System.Linq;
using System.Threading.Tasks;
using AiMeter.Providers;
using AiMeter.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiMeter.Tests;

public class OpenCodeProviderTests
{
    // Minimal slices of the real authenticated responses (captured from a live session).
    private const string GoLandingHtml =
        "<html>…<a href=\"/workspace/wrk_TEST123/go\"><span>Subscribe to Go</span></a>…</html>";

    private const string WorkspaceGoHtml =
        "…rollingUsage:$R[31]={status:\"ok\",resetInSec:13824,usagePercent:4}," +
        "weeklyUsage:$R[32]={status:\"ok\",resetInSec:122025,usagePercent:27}," +
        "monthlyUsage:$R[33]={status:\"ok\",resetInSec:2588405,usagePercent:13}…" +
        // decoy: a different object elsewhere carries monthlyUsage:null — must be ignored.
        "…billing:$R[36]={monthlyUsage:null,subscription:null}…";

    private static OpenCodeProvider Build(FakeOpenCodeSession session, FakeOpenCodeApiClient api) =>
        new(session, api, NullLogger<OpenCodeProvider>.Instance);

    [Fact]
    public async Task Logged_out_returns_auth_placeholder_and_makes_no_request()
    {
        var api = new FakeOpenCodeApiClient();
        var provider = Build(new FakeOpenCodeSession { HasSession = false }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Auth Required)");
        api.RequestedPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task Discovers_workspace_then_parses_three_usage_windows()
    {
        var api = new FakeOpenCodeApiClient()
            .When("/go", 200, GoLandingHtml)
            .When("/workspace/wrk_TEST123/go", 200, WorkspaceGoHtml);
        var provider = Build(new FakeOpenCodeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        // Two-step fetch: landing page first, then the discovered workspace page.
        api.RequestedPaths.Should().Equal("/go", "/workspace/wrk_TEST123/go");

        metrics.Should().HaveCount(3);

        // usagePercent is percent *used* → RemainingQuota = 100 − used.
        var rolling = metrics.Single(m => m.Name == "OpenCode Rolling");
        rolling.RemainingQuota.Should().Be(96);
        rolling.TotalQuota.Should().Be(100);
        rolling.ResetTime.Should().NotBeNull();

        metrics.Single(m => m.Name == "OpenCode Weekly").RemainingQuota.Should().Be(73);
        metrics.Single(m => m.Name == "OpenCode Monthly").RemainingQuota.Should().Be(87);
    }

    [Fact]
    public async Task Missing_workspace_link_surfaces_placeholder()
    {
        var api = new FakeOpenCodeApiClient().When("/go", 200, "<html>no workspace here</html>");
        var provider = Build(new FakeOpenCodeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (No Workspace)");
        api.RequestedPaths.Should().Equal("/go");
    }

    [Fact]
    public async Task Expired_session_is_cleared()
    {
        var session = new FakeOpenCodeSession { HasSession = true };
        var api = new FakeOpenCodeApiClient().When("/go", 403, null);
        var provider = Build(session, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Session Expired)");
        session.HasSession.Should().BeFalse();
    }
}
