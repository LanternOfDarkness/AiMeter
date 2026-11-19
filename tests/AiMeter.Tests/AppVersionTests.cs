using System;
using AiMeter.Services.Updates;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("2.1.0", 2, 1, 0, null)]
    [InlineData("v2.0.2", 2, 0, 2, null)]
    [InlineData("V2.1.0", 2, 1, 0, null)]
    [InlineData("2.1.0+4ae9bbe", 2, 1, 0, null)]
    [InlineData("2.1.0-beta.1", 2, 1, 0, "beta.1")]
    [InlineData("v2.1.0-rc.1+build.5", 2, 1, 0, "rc.1")]
    public void TryParse_accepts_release_tag_shapes(string text, int major, int minor, int patch, string? prerelease)
    {
        AppVersion.TryParse(text, out var version).Should().BeTrue();

        version.Should().Be(new AppVersion(major, minor, patch, prerelease));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2.1")]
    [InlineData("2")]
    [InlineData("2.1.0.0")]
    [InlineData("2.1.0-")]
    [InlineData("2.1.0- ")]
    [InlineData("1.0.x")]
    [InlineData("-1.0.0")]
    [InlineData("+1.0.0")]
    [InlineData("1.0.0 beta")]
    [InlineData(" 2.1.0")]
    [InlineData("2.1.0 ")]
    public void TryParse_rejects_malformed_tags(string? text)
    {
        AppVersion.TryParse(text, out var version).Should().BeFalse();
        version.Should().Be(default(AppVersion));
    }

    [Fact]
    public void Prerelease_follows_semver_precedence()
    {
        var ordered = new[]
        {
            "2.1.0-alpha",
            "2.1.0-alpha.1",
            "2.1.0-alpha.beta",
            "2.1.0-beta",
            "2.1.0-beta.2",
            "2.1.0-beta.11",
            "2.1.0-rc.1",
            "2.1.0",
        };

        for (var i = 1; i < ordered.Length; i++)
        {
            var lower = Parse(ordered[i - 1]);
            var higher = Parse(ordered[i]);

            lower.CompareTo(higher).Should().BeLessThan(0, "{0} < {1}", ordered[i - 1], ordered[i]);
            higher.CompareTo(lower).Should().BeGreaterThan(0, "{0} > {1}", ordered[i], ordered[i - 1]);
        }
    }

    [Fact]
    public void Release_is_higher_than_its_prerelease()
    {
        var release = Parse("2.1.0");
        var prerelease = Parse("2.1.0-rc.9");

        release.CompareTo(prerelease).Should().BeGreaterThan(0);
        prerelease.CompareTo(release).Should().BeLessThan(0);
    }

    [Fact]
    public void Numeric_prerelease_identifiers_lower_than_alphanumeric()
    {
        Parse("1.0.0-2").CompareTo(Parse("1.0.0-alpha")).Should().BeLessThan(0);
    }

    [Fact]
    public void Build_metadata_is_ignored_by_comparison()
    {
        var withBuild = Parse("2.1.0+4ae9bbe");
        var withoutBuild = Parse("2.1.0");

        withBuild.CompareTo(withoutBuild).Should().Be(0);
        withBuild.Should().Be(withoutBuild);
    }

    [Fact]
    public void Equals_agrees_with_compare_to_zero()
    {
        var versions = new[]
        {
            Parse("2.1.0"),
            Parse("v2.1.0"),
            Parse("V2.1.0"),
            Parse("2.1.0+4ae9bbe"),
        };

        foreach (var left in versions)
        {
            foreach (var right in versions)
            {
                left.CompareTo(right).Should().Be(0);
                left.Equals(right).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void ToString_renders_core_and_prerelease_only()
    {
        Parse("2.1.0+4ae9bbe").ToString().Should().Be("2.1.0");
        Parse("2.1.0-beta.1").ToString().Should().Be("2.1.0-beta.1");
    }

    private static AppVersion Parse(string text)
    {
        AppVersion.TryParse(text, out var version).Should().BeTrue();
        return version;
    }
}
