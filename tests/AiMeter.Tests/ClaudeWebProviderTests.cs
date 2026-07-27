using System.Linq;
using System.Threading.Tasks;
using AiMeter.Converters;
using AiMeter.Providers;
using AiMeter.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiMeter.Tests;

public class ClaudeWebProviderTests
{
    private const string OrgsJson = @"[
        { ""uuid"": ""org_12345"", ""name"": ""Test Org"" }
    ]";

    private const string UsageJsonWithExtraUsage = @"{
        ""limits"": [
            { ""kind"": ""session"", ""percent"": 20.0 },
            { ""kind"": ""weekly_all"", ""percent"": 45.0 },
            { ""kind"": ""extra_usage"", ""percent"": 15.0 }
        ],
        ""extra_usage"": {
            ""is_enabled"": true,
            ""used"": 10.0,
            ""limit"": 50.0
        }
    }";

    private const string UsageJsonWithTopLevelExtraUsageOnly = @"{
        ""limits"": [
            { ""kind"": ""session"", ""percent"": 30.0 }
        ],
        ""extra_usage"": {
            ""is_enabled"": true,
            ""used_cents"": 1500,
            ""limit_cents"": 5000
        }
    }";

    private static ClaudeWebProvider Build(FakeClaudeSession session, FakeClaudeApiClient api) =>
        new(session, api, NullLogger<ClaudeWebProvider>.Instance);

    [Fact]
    public async Task Logged_out_returns_auth_required()
    {
        var api = new FakeClaudeApiClient();
        var provider = Build(new FakeClaudeSession { HasSession = false }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("Auth Required");
        api.RequestedPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task Parses_limits_and_extra_usage()
    {
        var api = new FakeClaudeApiClient()
            .When("/api/organizations", 200, OrgsJson)
            .When("/api/organizations/org_12345/usage", 200, UsageJsonWithExtraUsage);
        var provider = Build(new FakeClaudeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().HaveCount(3);
        metrics.Single(m => m.Name == "Claude Session").RemainingQuota.Should().Be(80);
        metrics.Single(m => m.Name == "Claude Weekly").RemainingQuota.Should().Be(55);

        var extra = metrics.Single(m => m.Name == "Claude Extra Usage");
        extra.RemainingQuota.Should().Be(85);
        extra.WindowDuration.Should().Be(System.TimeSpan.FromDays(30));

        MetricLabelConverter.CodeFor(extra.Name).Should().Be("CEU");
    }

    [Fact]
    public async Task Parses_top_level_extra_usage_object_when_not_in_limits()
    {
        var api = new FakeClaudeApiClient()
            .When("/api/organizations", 200, OrgsJson)
            .When("/api/organizations/org_12345/usage", 200, UsageJsonWithTopLevelExtraUsageOnly);
        var provider = Build(new FakeClaudeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().HaveCount(2);
        metrics.Single(m => m.Name == "Claude Session").RemainingQuota.Should().Be(70);

        var extra = metrics.Single(m => m.Name == "Claude Extra Usage");
        extra.RemainingQuota.Should().Be(70); // used $15 out of $50 = 30% used -> 70% remaining
        extra.WindowDuration.Should().Be(System.TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task Null_monthly_limit_does_not_throw_exception()
    {
        const string jsonWithNullLimit = @"{
            ""limits"": [ { ""kind"": ""session"", ""percent"": 10.0 } ],
            ""extra_usage"": {
                ""is_enabled"": true,
                ""used"": null,
                ""monthly_limit"": null,
                ""percent"": 25.0
            }
        }";

        var api = new FakeClaudeApiClient()
            .When("/api/organizations", 200, OrgsJson)
            .When("/api/organizations/org_12345/usage", 200, jsonWithNullLimit);
        var provider = Build(new FakeClaudeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().HaveCount(2);
        metrics.Single(m => m.Name == "Claude Session").RemainingQuota.Should().Be(90);
        metrics.Single(m => m.Name == "Claude Extra Usage").RemainingQuota.Should().Be(75);
    }
}
