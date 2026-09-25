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

public class ProviderManagerNewProviderTests
{
    private static UsageMetric M(string name) => new() { Name = name, TotalQuota = 100, RemainingQuota = 50 };

    [Fact]
    public async Task Metrics_from_a_provider_logged_into_later_are_auto_selected()
    {
        var settings = new FakeSettingsManager();
        var claude = new FakeProvider { Name = "Claude", Metrics = new[] { M("Claude Session") } };
        var openCode = new FakeProvider { Name = "OpenCode", Metrics = new[] { M("OpenCode (Auth Required)") } };
        var manager = new ProviderManager(new IProvider[] { claude, openCode }, settings);

        await manager.RefreshAsync();
        manager.Metrics.Select(m => m.Name).Should().Equal("Claude Session");

        openCode.Metrics = new[] { M("OpenCode Rolling"), M("OpenCode Weekly") };
        await manager.RefreshAsync();

        settings.Current.SelectedMetrics.Should().Contain(new[] { "OpenCode Rolling", "OpenCode Weekly" });
        manager.Metrics.Select(m => m.Name).Should().Equal("Claude Session", "OpenCode Rolling", "OpenCode Weekly");
    }

    [Fact]
    public async Task A_seen_metric_the_user_unchecked_stays_unchecked()
    {
        var settings = new FakeSettingsManager();
        var provider = new FakeProvider { Metrics = new[] { M("A"), M("B") } };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);

        await manager.RefreshAsync();
        settings.Current.SelectedMetrics = new List<string> { "A" };

        await manager.RefreshAsync();

        settings.Current.SelectedMetrics.Should().Equal("A");
        manager.Metrics.Select(m => m.Name).Should().Equal("A");
    }

    [Fact]
    public async Task Upgrade_keeps_existing_choices_but_selects_a_provider_with_nothing_selected()
    {
        var settings = new FakeSettingsManager();
        settings.Current.SeenMetrics = null; // settings.json from before SeenMetrics existed
        settings.Current.SelectedMetrics = new List<string> { "Claude Session" };
        var claude = new FakeProvider { Name = "Claude", Metrics = new[] { M("Claude Session"), M("Claude Extra Usage") } };
        var openCode = new FakeProvider { Name = "OpenCode", Metrics = new[] { M("OpenCode Rolling") } };
        var manager = new ProviderManager(new IProvider[] { claude, openCode }, settings);

        await manager.RefreshAsync();

        settings.Current.SelectedMetrics.Should().BeEquivalentTo("Claude Session", "OpenCode Rolling");
    }
}
