using System;
using System.Linq;
using System.Threading.Tasks;
using AiMeter.Providers;
using AiMeter.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiMeter.Tests;

public class OpenCodeProviderTests
{
    private const string Workspace = "wrk_01KX5JXZQB94WAJNBNGER9A12C";

    private const string WorkspacesJson = """
        [
          {"id":"wrk_blocked","name":"Blocked","role":"owner","blocked":true},
          {"id":"wrk_01KX5JXZQB94WAJNBNGER9A12C","name":"Default","role":"owner","blocked":false}
        ]
        """;

    private const string GoStatusJson = """
        {
          "product": "go",
          "access": {
            "meters": {
              "fiveHour": {
                "resetsAt": "2026-11-19T23:17:05.000Z",
                "limitMicroCents": "1000000000",
                "usedMicroCents": "250000000"
              },
              "week": {
                "resetsAt": "2026-11-23T00:00:00.000Z",
                "limitMicroCents": "2000000000",
                "usedMicroCents": "1500000000"
              },
              "month": {
                "resetsAt": "2026-12-16T18:37:04.000Z",
                "limitMicroCents": "4000000000",
                "usedMicroCents": "1000000000"
              }
            }
          }
        }
        """;

    private static OpenCodeProvider Build(FakeOpenCodeSession session, FakeOpenCodeApiClient api) =>
        new(session, api, NullLogger<OpenCodeProvider>.Instance);

    private static FakeOpenCodeApiClient ApiWithUsage()
    {
        var api = new FakeOpenCodeApiClient();
        api.When(OpenCodeProvider.WorkspacesPath, 200, WorkspacesJson);
        api.When(OpenCodeProvider.GoStatusPath, 200, GoStatusJson);
        return api;
    }

    [Fact]
    public async Task Logged_out_returns_auth_placeholder_and_makes_no_request()
    {
        var api = new FakeOpenCodeApiClient();
        var provider = Build(new FakeOpenCodeSession { HasSession = false }, api);

        var result = await provider.GetMetricsAsync();

        result.Metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Auth Required)");
        result.Status.Should().Be(ProviderFetchStatus.NoSession);
        api.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Reads_go_meters_from_the_console_api()
    {
        var api = ApiWithUsage();
        var session = new FakeOpenCodeSession { HasSession = true };
        var provider = Build(session, api);

        var result = await provider.GetMetricsAsync();

        result.Status.Should().Be(ProviderFetchStatus.Success);
        result.Metrics.Select(m => (m.Name, m.RemainingQuota, m.WindowDuration)).Should().Equal(
            ("OpenCode Rolling", 75d, TimeSpan.FromHours(5)),
            ("OpenCode Weekly", 25d, TimeSpan.FromDays(7)),
            ("OpenCode Monthly", 75d, TimeSpan.FromDays(30)));
        result.Metrics.Should().OnlyContain(m => m.TotalQuota == 100);
        result.Metrics[0].ResetTime.Should().Be(new DateTimeOffset(2026, 11, 19, 23, 17, 5, TimeSpan.Zero).LocalDateTime);
        result.Metrics[1].ResetTime.Should().Be(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero).LocalDateTime);
        result.Metrics[2].ResetTime.Should().Be(new DateTimeOffset(2026, 12, 16, 18, 37, 4, TimeSpan.Zero).LocalDateTime);
        session.StatusNote.Should().BeNull();

        // First the workspace list, then the status with the selected workspace as x-org-id.
        api.Requests.Should().Equal(
            (OpenCodeProvider.WorkspacesPath, (string?)null),
            (OpenCodeProvider.GoStatusPath, (string?)Workspace));
    }

    [Fact]
    public async Task Caches_the_workspace_after_the_first_poll()
    {
        var api = ApiWithUsage();
        var provider = Build(new FakeOpenCodeSession { HasSession = true }, api);

        await provider.GetMetricsAsync();
        await provider.GetMetricsAsync();

        api.RequestedPaths.Should().Equal(
            OpenCodeProvider.WorkspacesPath,
            OpenCodeProvider.GoStatusPath,
            OpenCodeProvider.GoStatusPath);
        api.Requests[2].OrgId.Should().Be(Workspace);
    }

    [Fact]
    public async Task Skips_a_blocked_workspace()
    {
        var api = ApiWithUsage();
        var provider = Build(new FakeOpenCodeSession { HasSession = true }, api);

        await provider.GetMetricsAsync();

        api.Requests[1].OrgId.Should().Be(Workspace);
    }

    [Fact]
    public async Task Status_without_meters_means_no_go_subscription()
    {
        var api = new FakeOpenCodeApiClient()
            .When(OpenCodeProvider.WorkspacesPath, 200, WorkspacesJson)
            .When(OpenCodeProvider.GoStatusPath, 200, """{"product":"go","access":null}""");
        var session = new FakeOpenCodeSession { HasSession = true };
        var provider = Build(session, api);

        var result = await provider.GetMetricsAsync();

        result.Metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (No Subscription)");
        result.Status.Should().Be(ProviderFetchStatus.NoData);
        session.StatusNote.Should().Be(OpenCodeProvider.NoSubscriptionNote);
    }

    [Fact]
    public async Task Status_without_access_property_means_no_go_subscription()
    {
        var api = new FakeOpenCodeApiClient()
            .When(OpenCodeProvider.WorkspacesPath, 200, WorkspacesJson)
            .When(OpenCodeProvider.GoStatusPath, 200, """{"product":"go"}""");
        var session = new FakeOpenCodeSession { HasSession = true };

        var result = await Build(session, api).GetMetricsAsync();

        result.Metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (No Subscription)");
        result.Status.Should().Be(ProviderFetchStatus.NoData);
        session.StatusNote.Should().Be(OpenCodeProvider.NoSubscriptionNote);
    }

    [Theory]
    [InlineData("""{"access":{}}""")]
    [InlineData("""{"access":{"meters":[]}}""")]
    [InlineData("""{"access":{"meters":{"fiveHour":{"limitMicroCents":"0","usedMicroCents":"1"}}}}""")]
    public async Task Access_without_parseable_meters_is_an_error(string body)
    {
        var api = new FakeOpenCodeApiClient()
            .When(OpenCodeProvider.WorkspacesPath, 200, WorkspacesJson)
            .When(OpenCodeProvider.GoStatusPath, 200, body);
        var session = new FakeOpenCodeSession { HasSession = true };

        var result = await Build(session, api).GetMetricsAsync();

        result.Metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Error Fetching)");
        result.Status.Should().Be(ProviderFetchStatus.Error);
        session.StatusNote.Should().Be(OpenCodeProvider.ErrorNote);
    }

    [Fact]
    public async Task Unchanged_state_is_logged_only_once()
    {
        var api = new FakeOpenCodeApiClient()
            .When(OpenCodeProvider.WorkspacesPath, 200, WorkspacesJson)
            .When(OpenCodeProvider.GoStatusPath, 200, """{"access":null}""");
        var logger = new CountingLogger();
        var provider = new OpenCodeProvider(new FakeOpenCodeSession { HasSession = true }, api, logger);

        await provider.GetMetricsAsync();
        await provider.GetMetricsAsync();
        logger.Count(LogLevel.Information).Should().Be(1);

        api.When(OpenCodeProvider.GoStatusPath, 200, """{"access":{}}""");
        await provider.GetMetricsAsync();
        await provider.GetMetricsAsync();
        logger.Count(LogLevel.Warning).Should().Be(1);
    }

    private sealed class CountingLogger : ILogger<OpenCodeProvider>
    {
        private readonly System.Collections.Generic.List<LogLevel> _levels = new();
        public int Count(LogLevel level) => _levels.Count(l => l == level);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _levels.Add(logLevel);
    }

    [Fact]
    public async Task Unauthorized_clears_the_session()
    {
        var api = new FakeOpenCodeApiClient().When(OpenCodeProvider.WorkspacesPath, 401, """{"_tag":"Unauthorized"}""");
        var session = new FakeOpenCodeSession { HasSession = true };
        var provider = Build(session, api);

        var result = await provider.GetMetricsAsync();

        result.Metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Session Expired)");
        result.Status.Should().Be(ProviderFetchStatus.SessionExpired);
        session.HasSession.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_status_returns_an_error_then_recovers()
    {
        var api = new FakeOpenCodeApiClient().When(OpenCodeProvider.WorkspacesPath, 200, WorkspacesJson)
            .When(OpenCodeProvider.GoStatusPath, 500, "boom");
        var session = new FakeOpenCodeSession { HasSession = true };
        var provider = Build(session, api);

        var failed = await provider.GetMetricsAsync();
        failed.Metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Error Fetching)");
        failed.Status.Should().Be(ProviderFetchStatus.Error);
        session.StatusNote.Should().Be(OpenCodeProvider.ErrorNote);
        session.HasSession.Should().BeTrue();

        api.When(OpenCodeProvider.GoStatusPath, 200, GoStatusJson);
        var recovered = await provider.GetMetricsAsync();

        recovered.Status.Should().Be(ProviderFetchStatus.Success);
        session.StatusNote.Should().BeNull();
        api.RequestedPaths.Should().Equal(
            OpenCodeProvider.WorkspacesPath,
            OpenCodeProvider.GoStatusPath,
            OpenCodeProvider.WorkspacesPath,
            OpenCodeProvider.GoStatusPath);
    }

    [Fact]
    public async Task Malformed_json_returns_an_error_instead_of_throwing()
    {
        var api = new FakeOpenCodeApiClient().When(OpenCodeProvider.WorkspacesPath, 200, "{not json");
        var provider = Build(new FakeOpenCodeSession { HasSession = true }, api);

        var result = await provider.GetMetricsAsync();

        result.Metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode (Error Fetching)");
        result.Status.Should().Be(ProviderFetchStatus.Error);
    }

    [Fact]
    public void Skips_meters_without_a_usable_limit()
    {
        var json = """
            {"access":{"meters":{
              "fiveHour":{"limitMicroCents":"0","usedMicroCents":"1"},
              "week":{"limitMicroCents":null,"usedMicroCents":"1"},
              "month":{"limitMicroCents":"100","usedMicroCents":"25"}
            }}}
            """;

        var metrics = OpenCodeProvider.ParseGoStatus(json);

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenCode Monthly");
        metrics[0].RemainingQuota.Should().Be(75);
    }

    [Fact]
    public void Parses_numeric_limit_and_usage_values()
    {
        var json = """
            {"access":{"meters":{
              "fiveHour":{"limitMicroCents":1000,"usedMicroCents":250}
            }}}
            """;

        var metrics = OpenCodeProvider.ParseGoStatus(json);

        metrics.Should().ContainSingle();
        metrics[0].RemainingQuota.Should().Be(75);
        metrics[0].ResetTime.Should().BeNull();
    }

    [Fact]
    public void Workspace_list_without_ids_is_empty()
    {
        OpenCodeProvider.ParseFirstWorkspaceId("""[{"name":"Default"}]""").Should().BeNull();
        OpenCodeProvider.ParseFirstWorkspaceId("""{"id":"wrk_x"}""").Should().BeNull();
    }
}
