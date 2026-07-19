using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiMeter.Managers;
using AiMeter.Models;
using AiMeter.Providers;
using AiMeter.Tests.Fakes;
using AiMeter.ViewModels;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class SettingsViewModelCustomizationTests
{
    private static IReadOnlyList<UsageMetric> Sample() => new List<UsageMetric>
    {
        new() { Name = "A", TotalQuota = 100, RemainingQuota = 50 },
        new() { Name = "B", TotalQuota = 100, RemainingQuota = 50 },
    };

    private static SettingsViewModel BuildVm(FakeSettingsManager settings, ProviderManager manager)
        => new(settings, manager, new FakeClaudeSession(), new FakeOpenCodeSession(),
            new FakeStartupManager(), new FakeServiceProvider());

    [Fact]
    public async Task Save_persists_only_non_empty_label_and_color_overrides()
    {
        var settings = new FakeSettingsManager();
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);
        await manager.RefreshAsync();
        var vm = BuildVm(settings, manager);

        vm.MetricOptions.Single(o => o.Name == "A").CustomLabel = "Alpha";
        vm.MetricOptions.Single(o => o.Name == "B").CustomColor = "#2ECC71";

        vm.SaveCommand.Execute(null);

        settings.Current.MetricLabels.Should().ContainKey("A");
        settings.Current.MetricLabels["A"].Should().Be("Alpha");
        settings.Current.MetricLabels.ContainsKey("B").Should().BeFalse("B has no custom label");

        settings.Current.MetricColors.Should().ContainKey("B");
        settings.Current.MetricColors["B"].Should().Be("#2ECC71");
        settings.Current.MetricColors.ContainsKey("A").Should().BeFalse("A has no custom color");
    }

    [Fact]
    public async Task Save_trims_whitespace_around_a_custom_label()
    {
        var settings = new FakeSettingsManager();
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);
        await manager.RefreshAsync();
        var vm = BuildVm(settings, manager);

        vm.MetricOptions.Single(o => o.Name == "A").CustomLabel = "  Padded  ";

        vm.SaveCommand.Execute(null);

        settings.Current.MetricLabels["A"].Should().Be("Padded");
    }

    [Fact]
    public async Task ResyncMetricSelections_restores_saved_label_and_color_discarding_edits()
    {
        var settings = new FakeSettingsManager();
        settings.Current.MetricLabels = new Dictionary<string, string> { ["A"] = "Saved" };
        settings.Current.MetricColors = new Dictionary<string, string> { ["A"] = "#111111" };
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);
        await manager.RefreshAsync();
        var vm = BuildVm(settings, manager);

        vm.MetricOptions.Single(o => o.Name == "A").CustomLabel.Should().Be("Saved");
        vm.MetricOptions.Single(o => o.Name == "A").CustomColor.Should().Be("#111111");

        // Unsaved edits that Resync must discard.
        vm.MetricOptions.Single(o => o.Name == "A").CustomLabel = "Edited";
        vm.MetricOptions.Single(o => o.Name == "A").CustomColor = "#999999";
        vm.MetricOptions.Single(o => o.Name == "B").CustomLabel = "BeeEdit";

        vm.ResyncMetricSelections();

        vm.MetricOptions.Single(o => o.Name == "A").CustomLabel.Should().Be("Saved");
        vm.MetricOptions.Single(o => o.Name == "A").CustomColor.Should().Be("#111111");
        vm.MetricOptions.Single(o => o.Name == "B").CustomLabel.Should().BeEmpty();
        vm.MetricOptions.Single(o => o.Name == "B").CustomColor.Should().BeNull();
    }
}
