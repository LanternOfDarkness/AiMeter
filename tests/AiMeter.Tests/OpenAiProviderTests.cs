using System.Linq;
using System.Threading.Tasks;
using AiMeter.Providers;
using AiMeter.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiMeter.Tests;

public class OpenAiProviderTests
{
    // Kept in sync with OpenAiProvider's private CreditGrantsEndpoint constant.
    private const string CreditGrantsPath = "/v1/dashboard/billing/credit_grants";

    private const string CreditGrantsJson =
        "{\"object\":\"credit_summary\",\"total_granted\":50.0,\"total_used\":12.5," +
        "\"total_available\":37.5,\"grants\":{\"object\":\"list\",\"data\":[" +
        "{\"object\":\"credit_grant\",\"grant_amount\":50.0,\"used_amount\":12.5,\"expires_at\":1893456000}]}}";

    private static OpenAiProvider Build(FakeOpenAiSession session, FakeOpenAiApiClient api) =>
        new(session, api, NullLogger<OpenAiProvider>.Instance);

    [Fact]
    public async Task No_api_key_returns_placeholder_and_makes_no_request()
    {
        var api = new FakeOpenAiApiClient();
        var provider = Build(new FakeOpenAiSession { ApiKey = null }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenAI (API Key Required)");
        api.RequestedPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task Parses_credit_balance_and_forwards_the_key_as_bearer()
    {
        var api = new FakeOpenAiApiClient().When(CreditGrantsPath, 200, CreditGrantsJson);
        var provider = Build(new FakeOpenAiSession { ApiKey = "sk-test-123" }, api);

        var metrics = await provider.GetMetricsAsync();

        api.RequestedPaths.Should().Equal(CreditGrantsPath);
        api.BearerTokens.Should().Equal("sk-test-123");

        var credit = metrics.Should().ContainSingle().Which;
        credit.Name.Should().Be("OpenAI Credits");
        credit.TotalQuota.Should().Be(50.0);
        credit.RemainingQuota.Should().Be(37.5);
        credit.RemainingPercentage.Should().Be(75.0);
    }

    [Fact]
    public async Task Invalid_key_surfaces_message_without_clearing_the_key()
    {
        var session = new FakeOpenAiSession { ApiKey = "sk-bad" };
        var api = new FakeOpenAiApiClient().When(CreditGrantsPath, 401, null);
        var provider = Build(session, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenAI (Invalid Key)");
        // The key is user-managed — a 401 must not silently discard what the user entered.
        session.HasSession.Should().BeTrue();
    }

    [Fact]
    public async Task Project_key_without_billing_access_reports_no_billing_access()
    {
        var api = new FakeOpenAiApiClient().When(CreditGrantsPath, 403, null);
        var provider = Build(new FakeOpenAiSession { ApiKey = "sk-proj-x" }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenAI (No Billing Access)");
    }

    [Fact]
    public async Task Account_with_no_granted_credits_reports_no_credits()
    {
        // total_granted 0 → a 0/0 ring would misread as "exhausted"; provider guards against it.
        var body = "{\"object\":\"credit_summary\",\"total_granted\":0,\"total_used\":0,\"total_available\":0}";
        var api = new FakeOpenAiApiClient().When(CreditGrantsPath, 200, body);
        var provider = Build(new FakeOpenAiSession { ApiKey = "sk-payg" }, api);

        var metrics = await provider.GetMetricsAsync();

        metrics.Should().ContainSingle().Which.Name.Should().Be("OpenAI (No Credits)");
    }
}
