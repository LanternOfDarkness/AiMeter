using System;
using System.Collections.Generic;
using System.Linq;
using AiMeter.Managers;

namespace AiMeter.Services;

/// <summary>
/// Tracks whether an opencode.ai login has been completed. Mirrors <see cref="ClaudeSession"/>:
/// the real authenticated requests go through <see cref="IOpenCodeApiClient"/>'s browser-backed
/// WebView2 session, so this only needs to remember "have we logged in" for the UI and for
/// invalidating on 401/403.
/// </summary>
public class OpenCodeSession : IOpenCodeSession
{
    private readonly ISettingsManager _settingsManager;

    public OpenCodeSession(ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    public string ProviderName => "OpenCode";

    public bool HasSession => _settingsManager.Current.HasOpenCodeSession;

    public int LoginGeneration { get; private set; }

    public string? StatusNote { get; private set; }

    public event EventHandler? StatusNoteChanged;

    public void SetStatusNote(string? note)
    {
        if (note == StatusNote) return;
        StatusNote = note;
        StatusNoteChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Store(IEnumerable<(string Name, string Value)> cookies)
    {
        // opencode.ai uses an OAuth flow (opencode.ai/auth -> auth.opencode.ai -> callback),
        // so the exact session-cookie name isn't documented. The auth window only calls Store
        // once it has detected the post-login callback, so at that point a real session cookie
        // exists — marking on "any cookie present" is a name-agnostic signal. If the E.0 spike
        // identifies the exact cookie name, tighten this to Any(c => c.Name == "<name>").
        _settingsManager.Current.HasOpenCodeSession = cookies.Any();
        _settingsManager.Save();
        LoginGeneration++;
        SetStatusNote(null);
    }

    public void Clear()
    {
        _settingsManager.Current.HasOpenCodeSession = false;
        _settingsManager.Save();
        SetStatusNote(null);
    }
}
