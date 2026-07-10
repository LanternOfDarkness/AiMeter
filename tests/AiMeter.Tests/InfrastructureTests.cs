using AiMeter.Managers;
using AiMeter.Providers;
using AiMeter.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class InfrastructureTests
{
    [Fact]
    public void TracerBullet_real_ProviderManager_builds_with_fakes()
    {
        var settings = new FakeSettingsManager();
        var provider = new FakeProvider();
        var manager = new ProviderManager(new[] { (IProvider)provider }, settings);

        manager.Metrics.Should().NotBeNull();
        manager.KnownMetricNames.Should().BeEmpty();
    }
}