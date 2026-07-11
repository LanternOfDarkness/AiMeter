using System.Collections.Generic;
using System.Linq;
using AiMeter.Managers;

namespace AiMeter.Services;

/// <summary>
/// Tracks whether a Claude.ai login has been completed. The actual authenticated
/// requests go through <see cref="IClaudeApiClient"/>'s browser-backed session
/// (Cloudflare blocks a raw HttpClient even with a valid cookie), so this only
/// needs to remember "have we logged in" for the UI and for invalidating on 401/403.
/// </summary>
public class ClaudeSession : IClaudeSession
{
    private readonly ISettingsManager _settingsManager;

    public ClaudeSession(ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    public string ProviderName => "Claude";

    public bool HasSession => _settingsManager.Current.HasClaudeSession;

    public void Store(IEnumerable<(string Name, string Value)> cookies)
    {
        // The browser-backed WebView2 profile already persists the real cookies;
        // we only keep a lightweight marker so HasSession/IsLoggedIn work without
        // spinning up the WebView2 to check.
        _settingsManager.Current.HasClaudeSession = cookies.Any(c => c.Name == "sessionKey");
        _settingsManager.Save();
    }

    public void Clear()
    {
        _settingsManager.Current.HasClaudeSession = false;
        _settingsManager.Save();
    }
}
