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

public class SettingsViewModelResyncTests
{
    private static IReadOnlyList<UsageMetric> Sample() => new List<UsageMetric>
    {
        new() { Name = "A", TotalQuota = 100, RemainingQuota = 50 },
        new() { Name = "B", TotalQuota = 100, RemainingQuota = 50 },
        new() { Name = "C", TotalQuota = 100, RemainingQuota = 50 },
    };

    private static SettingsViewModel BuildVm(FakeSettingsManager settings, ProviderManager manager)
        => BuildVm(settings, manager, new FakeStartupManager());

    private static SettingsViewModel BuildVm(FakeSettingsManager settings, ProviderManager manager, FakeStartupManager startup)
    {
        var session = new FakeClaudeSession();
        var openCodeSession = new FakeOpenCodeSession();
        var openAiSession = new FakeOpenAiSession();
        var sp = new FakeServiceProvider();
        return new SettingsViewModel(settings, manager, session, openCodeSession, openAiSession, startup, sp);
    }

    [Fact]
    public void LaunchOnStartup_reflects_and_toggles_the_startup_manager()
    {
        var settings = new FakeSettingsManager();
        var manager = new ProviderManager(new IProvider[] { new FakeProvider() }, settings);
        var startup = new FakeStartupManager();
        var vm = BuildVm(settings, manager, startup);

        vm.LaunchOnStartup.Should().BeFalse();

        vm.LaunchOnStartup = true;
        startup.IsEnabled.Should().BeTrue();
        vm.LaunchOnStartup.Should().BeTrue();

        vm.LaunchOnStartup = false;
        startup.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ResyncMetricSelections_discards_unsaved_edits_and_restores_saved_state()
    {
        var settings = new FakeSettingsManager();
        // Saved state: only "A" is selected.
        settings.Current.SelectedMetrics = new List<string> { "A" };
        var provider = new FakeProvider { Metrics = Sample() };
        var manager = new ProviderManager(new IProvider[] { provider }, settings);

        // Populate KnownMetricNames before building the VM so the constructor lists them.
        await manager.RefreshAsync();

        var vm = BuildVm(settings, manager);
        vm.MetricOptions.Single(o => o.Name == "A").IsSelected.Should().BeTrue();
        vm.MetricOptions.Single(o => o.Name == "B").IsSelected.Should().BeFalse();
        vm.MetricOptions.Single(o => o.Name == "C").IsSelected.Should().BeFalse();

        // Simulate the user toggling checkboxes without pressing Save.
        vm.MetricOptions.Single(o => o.Name == "A").IsSelected = false;
        vm.MetricOptions.Single(o => o.Name == "B").IsSelected = true;
        vm.MetricOptions.Single(o => o.Name == "C").IsSelected = true;

        vm.ResyncMetricSelections();

        vm.MetricOptions.Single(o => o.Name == "A").IsSelected.Should().BeTrue();
        vm.MetricOptions.Single(o => o.Name == "B").IsSelected.Should().BeFalse();
        vm.MetricOptions.Single(o => o.Name == "C").IsSelected.Should().BeFalse();
    }
}