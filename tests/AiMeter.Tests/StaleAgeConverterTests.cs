using System.Globalization;
using AiMeter.Converters;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class StaleAgeConverterTests
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    private static object Age(DateTime? lastUpdated, DateTime now)
    {
        var conv = new StaleAgeConverter();
        return conv.Convert(new object?[] { lastUpdated, now }, typeof(string), null!, Ci);
    }

    [Fact]
    public void Null_timestamp_returns_empty()
    {
        Age(null, Now).Should().Be(string.Empty);
    }

    [Fact]
    public void Thirty_seconds_is_just_now()
    {
        Age(Now.AddSeconds(-30), Now).Should().Be("just now");
    }

    [Fact]
    public void Negative_delta_is_just_now()
    {
        Age(Now.AddMinutes(5), Now).Should().Be("just now");
    }

    [Fact]
    public void Fifty_nine_minutes_is_minutes()
    {
        Age(Now.AddMinutes(-59), Now).Should().Be("59m ago");
    }

    [Fact]
    public void Sixty_minutes_is_one_hour()
    {
        Age(Now.AddMinutes(-60), Now).Should().Be("1h ago");
    }

    [Fact]
    public void Twenty_three_hours_fifty_nine_minutes_is_hours()
    {
        Age(Now.AddHours(-23).AddMinutes(-59), Now).Should().Be("23h ago");
    }

    [Fact]
    public void Twenty_four_hours_is_one_day()
    {
        Age(Now.AddHours(-24), Now).Should().Be("1d ago");
    }
}
