using AiMeter.Services.Updates;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class AppInfoTests
{
    [Fact]
    public void Current_is_parsed_from_assembly_metadata()
    {
        AppInfo.Current.Should().NotBe(default(AppVersion));
    }

    [Fact]
    public void CurrentDisplay_has_v_prefix()
    {
        AppInfo.CurrentDisplay.Should().StartWith("v");
        AppInfo.CurrentDisplay.Should().Be("v" + AppInfo.Current);
    }

    [Fact]
    public void NormalizeInformationalVersion_strips_build_metadata()
    {
        var normalized = AppInfo.NormalizeInformationalVersion("2.1.0+4ae9bbe");

        normalized.Should().Be("2.1.0");
        AppVersion.TryParse(normalized, out var version).Should().BeTrue();
        version.Should().Be(new AppVersion(2, 1, 0));
    }

    [Fact]
    public void NormalizeInformationalVersion_strips_prerelease_build_metadata()
    {
        AppInfo.NormalizeInformationalVersion("2.1.0-beta.1+4ae9bbe").Should().Be("2.1.0-beta.1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+4ae9bbe")]
    public void NormalizeInformationalVersion_returns_null_when_nothing_remains(string? value)
    {
        AppInfo.NormalizeInformationalVersion(value).Should().BeNull();
    }
}
