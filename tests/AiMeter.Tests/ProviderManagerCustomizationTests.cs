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

public class ProviderManagerCustomizationTests
{
    private static IReadOnlyList<UsageMetric> Sample() => new List<UsageMetric>
    {
        new() { Name = "A", TotalQuota = 100, RemainingQuota = 50 },
        new() { Name = "B", TotalQuota = 100, RemainingQuota = 50 },
    };

    [Fact]
    public async Task SyncMetrics_applies_custom_label_and_color_from_config()
    {
        var settings = new FakeSettingsManager();
        settings.Current.MetricLabels = new Dictionary<string, string> { ["A"] = "My Label" };
        settings.Current.MetricColors = new Dictionary<string, string> { ["A"] = "#123456" };
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);

        await manager.RefreshAsync();

        var a = manager.Metrics.Single(m => m.Name == "A");
        a.DisplayName.Should().Be("My Label");
        a.CustomColor.Should().Be("#123456");
    }

    [Fact]
    public async Task SyncMetrics_defaults_display_name_to_name_and_color_to_null()
    {
        var settings = new FakeSettingsManager();
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);

        await manager.RefreshAsync();

        var b = manager.Metrics.Single(m => m.Name == "B");
        b.DisplayName.Should().Be("B", "with no override the label falls back to the raw name");
        b.CustomColor.Should().BeNull("with no override the bar uses the quota palette");
    }

    [Fact]
    public async Task RefilterMetrics_reapplies_customizations_after_a_config_change()
    {
        var settings = new FakeSettingsManager();
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);

        await manager.RefreshAsync();
        manager.Metrics.Single(m => m.Name == "A").DisplayName.Should().Be("A");

        settings.Current.MetricLabels = new Dictionary<string, string> { ["A"] = "Renamed" };
        manager.RefilterMetrics();

        manager.Metrics.Single(m => m.Name == "A").DisplayName.Should().Be("Renamed",
            "a Settings save re-filters from cache and must re-resolve labels with no network call");
    }

    [Fact]
    public async Task Whitespace_only_override_is_ignored()
    {
        var settings = new FakeSettingsManager();
        settings.Current.MetricLabels = new Dictionary<string, string> { ["A"] = "   " };
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);

        await manager.RefreshAsync();

        manager.Metrics.Single(m => m.Name == "A").DisplayName.Should().Be("A");
    }
}
