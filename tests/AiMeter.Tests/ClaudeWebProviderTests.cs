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

    // Captured verbatim (field-for-field) from a live claude.ai /usage response on a "Limited"
    // Extra Usage plan - see ClaudeWebProviderTests for why monthly_limit/used_credits need
    // decimal_places scaling and spend.limit is an object, not a bare number.
    private const string RealLimitedExtraUsageJson = @"{
        ""limits"": [
            { ""kind"": ""session"", ""percent"": 2 },
            { ""kind"": ""weekly_all"", ""percent"": 0 }
        ],
        ""extra_usage"": {
            ""is_enabled"": true,
            ""monthly_limit"": 7000,
            ""used_credits"": 5342.0,
            ""utilization"": 76.31428571428572,
            ""currency"": ""USD"",
            ""decimal_places"": 2
        },
        ""spend"": {
            ""used"": { ""amount_minor"": 5342, ""currency"": ""USD"", ""exponent"": 2 },
            ""limit"": { ""amount_minor"": 7000, ""currency"": ""USD"", ""exponent"": 2 },
            ""percent"": 76,
            ""enabled"": true,
            ""cap"": { ""money"": null, ""credits"": { ""amount_minor"": 7000, ""exponent"": 2 } },
            ""balance"": null
        }
    }";

    // Synthetic "Unlimited" plan shape: no monthly cap anywhere (monthly_limit/limit/cap all
    // null) and no server percent, with a prepaid credit balance still being drawn down.
    private const string UnlimitedExtraUsageWithBalanceJson = @"{
        ""limits"": [ { ""kind"": ""session"", ""percent"": 5 } ],
        ""extra_usage"": {
            ""is_enabled"": true,
            ""monthly_limit"": null,
            ""used_credits"": 5842.0,
            ""utilization"": null,
            ""currency"": ""USD"",
            ""decimal_places"": 2
        },
        ""spend"": {
            ""used"": { ""amount_minor"": 5842, ""currency"": ""USD"", ""exponent"": 2 },
            ""limit"": null,
            ""percent"": null,
            ""enabled"": true,
            ""cap"": { ""money"": null, ""credits"": null },
            ""balance"": { ""amount_minor"": 4158, ""currency"": ""USD"", ""exponent"": 2 }
        }
    }";

    private const string UnlimitedExtraUsageWithoutBalanceJson = @"{
        ""limits"": [ { ""kind"": ""session"", ""percent"": 5 } ],
        ""extra_usage"": {
            ""is_enabled"": true,
            ""monthly_limit"": null,
            ""used_credits"": 5842.0,
            ""utilization"": null,
            ""currency"": ""USD"",
            ""decimal_places"": 2
        },
        ""spend"": {
            ""used"": { ""amount_minor"": 5842, ""currency"": ""USD"", ""exponent"": 2 },
            ""limit"": null,
            ""percent"": null,
            ""enabled"": true,
            ""cap"": { ""money"": null, ""credits"": null },
            ""balance"": null
        }
    }";

    private const string ExtraUsageDisabledJson = @"{
        ""limits"": [ { ""kind"": ""session"", ""percent"": 5 } ],
        ""extra_usage"": { ""is_enabled"": false },
        ""spend"": { ""enabled"": false }
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

        // Money fields from the top-level extra_usage object should still enrich the
        // limits[]-array metric even though the array's own percent (not the money) drives
        // RemainingQuota here.
        extra.IsMoneyMetric.Should().BeTrue();
        extra.IsUnbounded.Should().BeFalse();
        extra.UsedAmount.Should().Be(10.0);
        extra.LimitAmount.Should().Be(50.0);

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

    [Fact]
    public async Task Real_limited_payload_uses_server_percent_and_scaled_dollar_amounts()
    {
        // Regression test for the bug this feature fixes: monthly_limit/used_credits are minor
        // units scaled by decimal_places (not already dollars), and spend.limit/spend.balance
        // are {amount_minor, exponent} objects, not bare numbers - so RemainingQuota used to
        // come out as ~99% instead of the correct ~24%.
        var api = new FakeClaudeApiClient()
            .When("/api/organizations", 200, OrgsJson)
            .When("/api/organizations/org_12345/usage", 200, RealLimitedExtraUsageJson);
        var provider = Build(new FakeClaudeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        var extra = metrics.Single(m => m.Name == "Claude Extra Usage");
        extra.RemainingQuota.Should().BeApproximately(23.6857, 0.01);
        extra.IsMoneyMetric.Should().BeTrue();
        extra.IsUnbounded.Should().BeFalse();
        extra.UsedAmount.Should().BeApproximately(53.42, 0.001);
        extra.LimitAmount.Should().BeApproximately(70.00, 0.001);
        extra.BalanceAmount.Should().BeNull();
        extra.Currency.Should().Be("USD");
        extra.ValueText.Should().Be("$53.42 / $70.00");
    }

    [Fact]
    public async Task Unlimited_plan_with_balance_shows_balance_left_and_no_percent_cap()
    {
        var api = new FakeClaudeApiClient()
            .When("/api/organizations", 200, OrgsJson)
            .When("/api/organizations/org_12345/usage", 200, UnlimitedExtraUsageWithBalanceJson);
        var provider = Build(new FakeClaudeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        var extra = metrics.Single(m => m.Name == "Claude Extra Usage");
        extra.IsMoneyMetric.Should().BeTrue();
        extra.IsUnbounded.Should().BeTrue();
        extra.LimitAmount.Should().BeNull();
        extra.UsedAmount.Should().BeApproximately(58.42, 0.001);
        extra.BalanceAmount.Should().BeApproximately(41.58, 0.001);
        extra.ValueText.Should().Be("$41.58 left");
        extra.RemainingQuota.Should().Be(100); // no cap to be "low" against
    }

    [Fact]
    public async Task Unlimited_plan_without_balance_shows_amount_spent()
    {
        var api = new FakeClaudeApiClient()
            .When("/api/organizations", 200, OrgsJson)
            .When("/api/organizations/org_12345/usage", 200, UnlimitedExtraUsageWithoutBalanceJson);
        var provider = Build(new FakeClaudeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        var extra = metrics.Single(m => m.Name == "Claude Extra Usage");
        extra.IsUnbounded.Should().BeTrue();
        extra.BalanceAmount.Should().BeNull();
        extra.ValueText.Should().Be("$58.42 used");
    }

    [Fact]
    public async Task Disabled_extra_usage_yields_no_metric()
    {
        var api = new FakeClaudeApiClient()
            .When("/api/organizations", 200, OrgsJson)
            .When("/api/organizations/org_12345/usage", 200, ExtraUsageDisabledJson);
        var provider = Build(new FakeClaudeSession { HasSession = true }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().NotContain(m => m.Name == "Claude Extra Usage");
    }
}
