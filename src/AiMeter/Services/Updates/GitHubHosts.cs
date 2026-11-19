using System;

namespace AiMeter.Services.Updates;

/// <summary>Host allow-list for every GitHub URL the updater talks to.</summary>
public static class GitHubHosts
{
    private static readonly string[] AllowedHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "github-releases.githubusercontent.com",
    ];

    /// <summary>True for an absolute https URI on an allowed GitHub host with no userinfo and the default port.</summary>
    public static bool IsAllowed(Uri uri)
    {
        if (uri is not { IsAbsoluteUri: true }) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (!uri.IsDefaultPort) return false;

        return IsAllowedHost(uri.Host);
    }

    private static bool IsAllowedHost(string host)
    {
#if DEBUG
        if (IsLocalDevHost(host)) return true;
#endif

        foreach (var allowed in AllowedHosts)
        {
            if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

#if DEBUG
    private static bool IsLocalDevHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }
#endif
}
