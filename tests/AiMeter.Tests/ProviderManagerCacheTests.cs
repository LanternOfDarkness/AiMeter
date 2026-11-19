using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiMeter.Managers;
using AiMeter.Models;
using AiMeter.Providers;
using AiMeter.Services;
using AiMeter.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class ProviderManagerCacheTests
{
    private static IReadOnlyList<UsageMetric> Sample(params string[] names) =>
        names.Select(name => new UsageMetric { Name = name, TotalQuota = 100, RemainingQuota = 50 }).ToList();

    private static UsageMetric Single(string name) =>
        new() { Name = name, TotalQuota = 100, RemainingQuota = 50 };

    [Fact]
    public async Task Error_falls_back_to_last_success_and_marks_metrics_stale()
    {
        var settings = new FakeSettingsManager();
        var cache = new FakeMetricCacheStore();
        var provider = new FakeProvider { Metrics = Sample("A") };
        var manager = new ProviderManager(new IProvider[] { provider }, settings, cacheStore: cache);

        await manager.RefreshAsync();

        var fetchedAt = manager.Metrics.Single().LastUpdatedUtc;
        fetchedAt.Should().NotBeNull();
        manager.Metrics.Single().IsStale.Should().BeFalse();

        provider.Status = ProviderFetchStatus.Error;

        await manager.RefreshAsync();

        manager.Metrics.Should().ContainSingle();
        manager.Metrics[0].Name.Should().Be("A");
        manager.Metrics[0].RemainingQuota.Should().Be(50);
        manager.Metrics[0].IsStale.Should().BeTrue();
        manager.Metrics[0].LastUpdatedUtc.Should().Be(fetchedAt, "the age must reflect the last successful fetch");

        cache.TryGet("Fake", out var snapshot).Should().BeTrue();
        snapshot.Metrics.Select(m => m.Name).Should().Equal("A");
    }

    [Fact]
    public async Task Later_success_clears_stale_advances_timestamp_and_drops_removed_metrics()
    {
        var settings = new FakeSettingsManager();
        var cache = new FakeMetricCacheStore();
        var provider = new FakeProvider { Metrics = Sample("A", "B") };
        var manager = new ProviderManager(new IProvider[] { provider }, settings, cacheStore: cache);

        await manager.RefreshAsync();
        var firstFetch = manager.Metrics[0].LastUpdatedUtc;

        provider.Status = ProviderFetchStatus.Error;
        await manager.RefreshAsync();
        manager.Metrics.Should().OnlyContain(m => m.IsStale);

        await Task.Delay(10);
        provider.Status = ProviderFetchStatus.Success;
        provider.Metrics = Sample("A");

        await manager.RefreshAsync();

        manager.Metrics.Select(m => m.Name).Should().Equal("A");
        manager.Metrics[0].IsStale.Should().BeFalse();
        manager.Metrics[0].LastUpdatedUtc.Should().BeAfter(firstFetch!.Value);

        cache.TryGet("Fake", out var snapshot).Should().BeTrue();
        snapshot.Metrics.Select(m => m.Name).Should().Equal(new[] { "A" },
            "a successful fetch replaces the whole snapshot so a vanished meter is not resurrected");
    }

    [Theory]
    [InlineData(ProviderFetchStatus.NoSession)]
    [InlineData(ProviderFetchStatus.SessionExpired)]
    [InlineData(ProviderFetchStatus.NoData)]
    public async Task Auth_and_no_data_statuses_remove_the_cache_and_show_no_stale_metrics(
        ProviderFetchStatus status)
    {
        var settings = new FakeSettingsManager();
        var cache = new FakeMetricCacheStore();
        var provider = new FakeProvider { Metrics = Sample("A") };
        var manager = new ProviderManager(new IProvider[] { provider }, settings, cacheStore: cache);

        await manager.RefreshAsync();
        cache.TryGet("Fake", out _).Should().BeTrue();

        provider.Status = status;
        provider.Metrics = Sample("Fake (Auth Required)");

        await manager.RefreshAsync();

        cache.TryGet("Fake", out _).Should().BeFalse();
        manager.Metrics.Should().OnlyContain(m => !m.IsStale);
        manager.Metrics.Should().NotContain(m => m.Name == "A");
    }

    [Fact]
    public void Startup_restore_shows_stale_metrics_and_sets_HasFetchedOnce()
    {
        var settings = new FakeSettingsManager();
        var cache = new FakeMetricCacheStore();
        var fetchedAt = DateTime.UtcNow.AddMinutes(-5);
        cache.Entries["Fake"] = new CachedProviderMetrics(Sample("A", "B"), fetchedAt);

        var provider = new FakeProvider();
        var manager = new ProviderManager(new IProvider[] { provider }, settings, cacheStore: cache);

        manager.HasFetchedOnce.Should().BeTrue();
        manager.Metrics.Select(m => m.Name).Should().Equal("A", "B");
        manager.Metrics.Should().OnlyContain(m => m.IsStale);
        manager.Metrics.Should().OnlyContain(m => m.LastUpdatedUtc == fetchedAt);
        provider.GetMetricsAsyncCallCount.Should().Be(0, "restoring must not fetch");
    }

    [Fact]
    public async Task Restore_and_stale_refresh_raise_no_alerts()
    {
        var settings = new FakeSettingsManager();
        settings.Current.NotificationsEnabled = true;
        settings.Current.NotifyOnLowQuota = true;
        settings.Current.LowQuotaThreshold = 60;

        var cache = new FakeMetricCacheStore();
        cache.Entries["Fake"] = new CachedProviderMetrics(
            new List<UsageMetric> { new() { Name = "A", TotalQuota = 100, RemainingQuota = 10 } },
            DateTime.UtcNow.AddMinutes(-5));

        var provider = new FakeProvider { Status = ProviderFetchStatus.Error };
        var manager = new ProviderManager(new IProvider[] { provider }, settings, cacheStore: cache);

        var alerts = 0;
        manager.AlertRaised += (_, _) => alerts++;

        await manager.RefreshAsync();

        alerts.Should().Be(0, "restored or repeated stale values are not new readings");
    }

    [Fact]
    public void RefilterMetrics_works_from_a_stale_restored_state()
    {
        var settings = new FakeSettingsManager();
        var cache = new FakeMetricCacheStore();
        cache.Entries["Fake"] = new CachedProviderMetrics(
            Sample("A", "B", "C"),
            DateTime.UtcNow.AddMinutes(-5));

        var provider = new FakeProvider();
        var manager = new ProviderManager(new IProvider[] { provider }, settings, cacheStore: cache);

        manager.Metrics.Select(m => m.Name).Should().Equal("A", "B", "C");

        settings.Current.SelectedMetrics = new List<string> { "A" };
        manager.RefilterMetrics();

        manager.Metrics.Select(m => m.Name).Should().Equal("A");
        manager.Metrics[0].IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task Exceptions_are_treated_as_the_error_path()
    {
        var settings = new FakeSettingsManager();
        var cache = new FakeMetricCacheStore();
        cache.Entries["Boom"] = new CachedProviderMetrics(Sample("A"), DateTime.UtcNow.AddMinutes(-5));

        var restored = new ProviderManager(new IProvider[] { new ThrowingProvider() }, settings, cacheStore: cache);
        await restored.RefreshAsync();

        restored.Metrics.Select(m => m.Name).Should().Equal("A");
        restored.Metrics[0].IsStale.Should().BeTrue();

        var emptyCache = new FakeMetricCacheStore();
        var fallback = new ProviderManager(new IProvider[] { new ThrowingProvider() }, new FakeSettingsManager(), cacheStore: emptyCache);
        await fallback.RefreshAsync();

        fallback.Metrics.Should().ContainSingle();
        fallback.Metrics[0].Name.Should().Be("Boom (Error Fetching)");
        fallback.Metrics[0].IsStale.Should().BeFalse();
    }

    private sealed class ThrowingProvider : IProvider
    {
        public string Name => "Boom";

        public Task<ProviderFetchResult> GetMetricsAsync()
            => throw new InvalidOperationException("simulated failure");
    }
}
