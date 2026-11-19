using System;
using System.Globalization;

namespace AiMeter.Services.Updates;

/// <summary>
/// Semantic version (SemVer 2.0) value type used for release tags and app identity.
/// </summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch, string? Prerelease = null)
    : IComparable<AppVersion>
{
    /// <summary>
    /// Parses tags such as <c>v2.1.0</c>, <c>2.1.0+4ae9bbe</c> and <c>2.1.0-beta.1</c>.
    /// Build metadata is ignored and an empty prerelease is normalized to <see langword="null"/>.
    /// </summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(text)) return false;

        var span = text.AsSpan();
        if (char.IsWhiteSpace(span[0]) || char.IsWhiteSpace(span[^1])) return false;
        if (span[0] is 'v' or 'V') span = span[1..];
        if (span.IsEmpty) return false;

        var plus = span.IndexOf('+');
        if (plus >= 0) span = span[..plus];

        string? prerelease = null;
        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            var pre = span[(dash + 1)..].Trim();
            if (pre.IsEmpty) return false;
            prerelease = pre.ToString();
            span = span[..dash];
        }

        if (!TryReadNumericParts(span, out var major, out var minor, out var patch)) return false;

        version = new AppVersion(major, minor, patch, prerelease);
        return true;
    }

    /// <summary>SemVer 2.0 precedence; build metadata is not part of the comparison.</summary>
    public int CompareTo(AppVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        return ComparePrerelease(NormalizePrerelease(Prerelease), NormalizePrerelease(other.Prerelease));
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var core = $"{Major}.{Minor}.{Patch}";
        return string.IsNullOrEmpty(Prerelease) ? core : $"{core}-{Prerelease}";
    }

    private static bool TryReadNumericParts(ReadOnlySpan<char> span, out int major, out int minor, out int patch)
    {
        major = minor = patch = 0;
        var index = 0;

        for (var part = 0; part < 3; part++)
        {
            if (part > 0)
            {
                if (index >= span.Length || span[index] != '.') return false;
                index++;
            }

            var start = index;
            while (index < span.Length && span[index] != '.') index++;

            var slice = span[start..index];
            if (slice.IsEmpty) return false;
            if (!IsAllDigits(slice)) return false;
            if (!int.TryParse(slice, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return false;

            switch (part)
            {
                case 0: major = value; break;
                case 1: minor = value; break;
                default: patch = value; break;
            }
        }

        return index == span.Length;
    }

    private static string? NormalizePrerelease(string? prerelease) =>
        string.IsNullOrWhiteSpace(prerelease) ? null : prerelease;

    private static int ComparePrerelease(string? left, string? right)
    {
        if (left is null) return right is null ? 0 : 1;
        if (right is null) return -1;

        var leftSpan = left.AsSpan();
        var rightSpan = right.AsSpan();

        while (true)
        {
            var leftDot = leftSpan.IndexOf('.');
            var rightDot = rightSpan.IndexOf('.');

            var leftIdentifier = leftDot < 0 ? leftSpan : leftSpan[..leftDot];
            var rightIdentifier = rightDot < 0 ? rightSpan : rightSpan[..rightDot];

            var result = CompareIdentifier(leftIdentifier, rightIdentifier);
            if (result != 0) return result;

            if (leftDot < 0) return rightDot < 0 ? 0 : -1;
            if (rightDot < 0) return 1;

            leftSpan = leftSpan[(leftDot + 1)..];
            rightSpan = rightSpan[(rightDot + 1)..];
        }
    }

    private static int CompareIdentifier(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        var leftNumeric = IsAllDigits(left);
        var rightNumeric = IsAllDigits(right);

        if (leftNumeric && rightNumeric) return CompareNumericIdentifier(left, right);
        if (leftNumeric) return -1;
        if (rightNumeric) return 1;

        return left.SequenceCompareTo(right);
    }

    private static int CompareNumericIdentifier(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        left = TrimLeadingZeros(left);
        right = TrimLeadingZeros(right);

        if (left.Length != right.Length) return left.Length.CompareTo(right.Length);
        return left.SequenceCompareTo(right);
    }

    private static ReadOnlySpan<char> TrimLeadingZeros(ReadOnlySpan<char> span)
    {
        var index = 0;
        while (index < span.Length - 1 && span[index] == '0') index++;
        return span[index..];
    }

    private static bool IsAllDigits(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty) return false;
        foreach (var c in span)
        {
            if (c is < '0' or > '9') return false;
        }

        return true;
    }
}
