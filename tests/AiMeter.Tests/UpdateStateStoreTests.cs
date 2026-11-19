using System;
using System.IO;
using AiMeter.Services.Updates;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class UpdateStateStoreTests
{
    private static string NewTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AiMeterUpdateStateTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void Save_and_load_round_trip_all_fields()
    {
        var directory = NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "update-state.json");
            var notifiedAt = new DateTimeOffset(2026, 11, 19, 9, 14, 3, TimeSpan.Zero);
            var checkedAt = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

            var store = new FileUpdateStateStore(path);
            store.Save(new UpdateState
            {
                SkippedVersion = "2.1.0",
                LastNotifiedVersion = "2.1.0",
                LastNotifiedUtc = notifiedAt,
                LastCheckUtc = checkedAt,
            });

            File.Exists(path).Should().BeTrue();
            File.Exists(path + ".tmp").Should().BeFalse();

            var loaded = new FileUpdateStateStore(path).Load();

            loaded.SkippedVersion.Should().Be("2.1.0");
            loaded.LastNotifiedVersion.Should().Be("2.1.0");
            loaded.LastNotifiedUtc.Should().Be(notifiedAt);
            loaded.LastCheckUtc.Should().Be(checkedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Second_save_replaces_the_first_atomically()
    {
        var directory = NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "update-state.json");
            var store = new FileUpdateStateStore(path);

            store.Save(new UpdateState { SkippedVersion = "2.0.0" });
            store.Save(new UpdateState { SkippedVersion = "2.1.0", LastCheckUtc = DateTimeOffset.UtcNow });

            var loaded = store.Load();
            loaded.SkippedVersion.Should().Be("2.1.0");
            loaded.LastCheckUtc.Should().NotBeNull();
            File.Exists(path + ".tmp").Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_yields_an_empty_state()
    {
        var directory = NewTempDirectory();
        try
        {
            var loaded = new FileUpdateStateStore(Path.Combine(directory, "update-state.json")).Load();

            loaded.SkippedVersion.Should().BeNull();
            loaded.LastNotifiedVersion.Should().BeNull();
            loaded.LastNotifiedUtc.Should().BeNull();
            loaded.LastCheckUtc.Should().BeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_json_falls_back_to_an_empty_state()
    {
        var directory = NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "update-state.json");
            File.WriteAllText(path, "{ this is not json");

            var loaded = new FileUpdateStateStore(path).Load();

            loaded.SkippedVersion.Should().BeNull();
            loaded.LastNotifiedVersion.Should().BeNull();
            loaded.LastNotifiedUtc.Should().BeNull();
            loaded.LastCheckUtc.Should().BeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_creates_the_target_directory()
    {
        var directory = NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "nested", "deeper", "update-state.json");

            new FileUpdateStateStore(path).Save(new UpdateState { SkippedVersion = "3.0.0" });

            File.Exists(path).Should().BeTrue();
            new FileUpdateStateStore(path).Load().SkippedVersion.Should().Be("3.0.0");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
