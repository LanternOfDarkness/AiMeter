using System;
using System.Reflection;

namespace AiMeter.Services.Updates;

/// <summary>
/// Identity of the running app, derived from the assembly version metadata. Has no WPF
/// dependency so it can be used by services and tests.
/// </summary>
public static class AppInfo
{
    /// <summary>The running app version, without source-revision build metadata.</summary>
    public static AppVersion Current { get; } = ResolveCurrent();

    /// <summary>Version rendered for UI, e.g. <c>v2.1.0</c>.</summary>
    public static string CurrentDisplay => "v" + Current;

    /// <summary>
    /// Cuts informational version metadata at the first <c>+</c> and trims it. Returns
    /// <see langword="null"/> when nothing usable remains.
    /// </summary>
    public static string? NormalizeInformationalVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var plus = value.IndexOf('+');
        var core = (plus < 0 ? value : value[..plus]).Trim();
        return core.Length == 0 ? null : core;
    }

    private static AppVersion ResolveCurrent()
    {
        var assembly = typeof(AppInfo).Assembly;

        try
        {
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var normalized = NormalizeInformationalVersion(informational);
            if (normalized is not null && AppVersion.TryParse(normalized, out var parsed)) return parsed;
        }
        catch
        {
        }

        try
        {
            var fallback = assembly.GetName().Version;
            if (fallback is not null)
            {
                var text = $"{fallback.Major}.{fallback.Minor}.{fallback.Build}";
                if (AppVersion.TryParse(text, out var parsed)) return parsed;
            }
        }
        catch
        {
        }

        return default;
    }
}
