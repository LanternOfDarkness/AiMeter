using System;
using System.Collections.Generic;
using System.IO;
using AiMeter.Models;
using AiMeter.Services;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class FileMetricCacheStoreTests
{
    private static string NewTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AiMeterCacheTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void Round_trip_preserves_every_cached_field_and_skips_app_state()
    {
        var directory = NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "cache.json");
            var fetchedAt = new DateTime(2026, 11, 19, 9, 14, 3, 221, DateTimeKind.Utc);
            var resetTime = new DateTime(2026, 11, 26, 2, 0, 0, DateTimeKind.Utc);

            var metric = new UsageMetric
            {
                Name = "Claude Extra Usage",
                TotalQuota = 200,
                RemainingQuota = 54.5,
                ResetTime = resetTime,
                WindowDuration = TimeSpan.FromDays(7),
                IsMoneyMetric = true,
                IsUnbounded = false,
                UsedAmount = 3.25,
                LimitAmount = 10,
                BalanceAmount = 1.5,
                Currency = "USD",
                DisplayName = "Custom label",
                CustomColor = "#ABCDEF",
                IsStale = true,
                LastUpdatedUtc = DateTime.UtcNow,
            };

            var store = new FileMetricCacheStore(path);
            store.Set("Claude", new List<UsageMetric> { metric }, fetchedAt);
            store.Save();

            File.Exists(path).Should().BeTrue();
            File.Exists(path + ".tmp").Should().BeFalse("the temp file is moved over the cache atomically");

            var json = File.ReadAllText(path);
            json.Should().Contain("\"version\": 1");
            json.Should().Contain("\"fetchedAtUtc\"");
            json.Should().NotContainEquivalentOf("\"displayName\"");
            json.Should().NotContainEquivalentOf("\"isStale\"");
            json.Should().NotContainEquivalentOf("\"lastUpdatedUtc\"");

            var loaded = new FileMetricCacheStore(path);
            loaded.Load();

            loaded.TryGet("Claude", out var snapshot).Should().BeTrue();
            snapshot.FetchedAtUtc.Should().Be(fetchedAt);
            snapshot.Metrics.Should().ContainSingle();

            var restored = snapshot.Metrics[0];
            restored.Name.Should().Be("Claude Extra Usage");
            restored.TotalQuota.Should().Be(200);
            restored.RemainingQuota.Should().Be(54.5);
            restored.ResetTime.Should().Be(resetTime);
            restored.WindowDuration.Should().Be(TimeSpan.FromDays(7));
            restored.IsMoneyMetric.Should().BeTrue();
            restored.IsUnbounded.Should().BeFalse();
            restored.UsedAmount.Should().Be(3.25);
            restored.LimitAmount.Should().Be(10);
            restored.BalanceAmount.Should().Be(1.5);
            restored.Currency.Should().Be("USD");
            restored.DisplayName.Should().BeEmpty("display name is app state, not cached provider data");
            restored.CustomColor.Should().BeNull();
            restored.IsStale.Should().BeFalse();
            restored.LastUpdatedUtc.Should().BeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Absent_file_loads_empty_without_throwing()
    {
        var directory = NewTempDirectory();
        try
        {
            var store = new FileMetricCacheStore(Path.Combine(directory, "missing.json"));

            store.Load();

            store.TryGet("Fake", out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_file_loads_empty_without_throwing()
    {
        var directory = NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "cache.json");
            File.WriteAllText(path, "{ this is not json");

            var store = new FileMetricCacheStore(path);

            store.Load();

            store.TryGet("Fake", out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Unsupported_version_loads_empty_without_throwing()
    {
        var directory = NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "cache.json");
            File.WriteAllText(path, "{\"version\": 99, \"providers\": {}}");

            var store = new FileMetricCacheStore(path);

            store.Load();

            store.TryGet("Fake", out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Removed_entries_disappear_after_save_and_reload()
    {
        var directory = NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "cache.json");
            var store = new FileMetricCacheStore(path);
            store.Set("Fake", new List<UsageMetric> { new() { Name = "A" } }, DateTime.UtcNow);
            store.Save();

            store.Remove("Fake");
            store.Save();

            var loaded = new FileMetricCacheStore(path);
            loaded.Load();

            loaded.TryGet("Fake", out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
