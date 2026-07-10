using System.Collections.Generic;
using System.Threading.Tasks;
using AiMeter.Managers;
using AiMeter.Models;
using AiMeter.Providers;
using AiMeter.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class ProviderManagerHasFetchedOnceTests
{
    private static IReadOnlyList<UsageMetric> Sample() => new List<UsageMetric>
    {
        new() { Name = "A", TotalQuota = 100, RemainingQuota = 50 },
    };

    [Fact]
    public void HasFetchedOnce_is_false_before_any_refresh()
    {
        var settings = new FakeSettingsManager();
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);

        manager.HasFetchedOnce.Should().BeFalse();
    }

    [Fact]
    public async Task HasFetchedOnce_is_true_after_the_first_refresh_completes()
    {
        var settings = new FakeSettingsManager();
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);

        await manager.RefreshAsync();

        manager.HasFetchedOnce.Should().BeTrue();
    }
}