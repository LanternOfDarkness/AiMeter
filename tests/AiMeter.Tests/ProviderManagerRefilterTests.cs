using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiMeter.Managers;
using AiMeter.Models;
using AiMeter.Providers;
using AiMeter.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class ProviderManagerRefilterTests
{
    private static IReadOnlyList<UsageMetric> Sample() => new List<UsageMetric>
    {
        new() { Name = "A", TotalQuota = 100, RemainingQuota = 50 },
        new() { Name = "B", TotalQuota = 100, RemainingQuota = 50 },
        new() { Name = "C", TotalQuota = 100, RemainingQuota = 50 },
    };

    [Fact]
    public async Task RefilterMetrics_does_not_call_providers_filter_uses_cache_only()
    {
        var settings = new FakeSettingsManager();
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);

        await manager.RefreshAsync();
        var fetchesAfterRefresh = provider.GetMetricsAsyncCallCount;

        settings.Current.SelectedMetrics = new List<string> { "A" };

        manager.RefilterMetrics();

        provider.GetMetricsAsyncCallCount.Should().Be(fetchesAfterRefresh,
            "RefilterMetrics must re-filter cached data without any network fetch");
    }

    [Fact]
    public async Task RefilterMetrics_shows_only_selected_metrics_after_narrowing()
    {
        var settings = new FakeSettingsManager();
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);

        await manager.RefreshAsync();
        manager.Metrics.Select(m => m.Name).Should().Equal("A", "B", "C");

        settings.Current.SelectedMetrics = new List<string> { "A" };
        manager.RefilterMetrics();

        manager.Metrics.Select(m => m.Name).Should().Equal("A");
    }

    [Fact]
    public async Task RefilterMetrics_restores_a_reenabled_metric_from_cache()
    {
        var settings = new FakeSettingsManager();
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);

        await manager.RefreshAsync();

        settings.Current.SelectedMetrics = new List<string> { "A" };
        manager.RefilterMetrics();
        manager.Metrics.Select(m => m.Name).Should().Equal("A");

        // Re-enable everything by clearing the selection (empty = show all).
        settings.Current.SelectedMetrics = new List<string>();
        manager.RefilterMetrics();

        manager.Metrics.Select(m => m.Name).Should().Equal(new[] { "A", "B", "C" },
            "the last-good snapshot must retain every metric ever fetched so a re-enabled one reappears immediately");
    }
}