using System;
using AiMeter.Managers;
using AiMeter.Services.Updates;
using AiMeter.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AiMeter.ViewModels;

/// <summary>
/// Backs the tray balloon that proposes an available update. It only closes the balloon and
/// opens the update window; the download still requires an explicit click there.
/// </summary>
public sealed partial class UpdateBalloonViewModel : ObservableObject
{
    private const int MaxNotesLength = 180;

    private readonly IUpdateManager _manager;
    private readonly UpdateWindow? _window;
    private readonly ILogger<UpdateBalloonViewModel>? _logger;
    private ReleaseInfo? _release;
    private Action? _close;

    public UpdateBalloonViewModel(
        IUpdateManager manager,
        UpdateWindow window,
        IUiDispatcher dispatcher,
        ILogger<UpdateBalloonViewModel>? logger = null)
    {
        _manager = manager;
        _window = window;
        _logger = logger;
    }

    /// <summary>Title line shown in the balloon, e.g. "AiMeter v2.1.0 is available".</summary>
    public string VersionText =>
        _release is { } release ? "AiMeter v" + release.Version + " is available" : string.Empty;

    /// <summary>Whitespace-collapsed, truncated release notes, or a generic fallback.</summary>
    public string NotesSummary => Summarize(_release?.Notes);

    /// <summary>Stores the release to show and the callback that closes the balloon.</summary>
    public void Initialize(ReleaseInfo release, Action close)
    {
        _release = release;
        _close = close;

        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(NotesSummary));
    }

    [RelayCommand]
    private void Update()
    {
        if (_release is null) return;

        _close?.Invoke();
        _window?.ShowOrActivate();
    }

    [RelayCommand]
    private void Later()
    {
        if (_release is null) return;

        try
        {
            _manager.DismissNotification();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not dismiss the update notification.");
        }

        _close?.Invoke();
    }

    internal static string Summarize(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "A new version is available.";

        // GitHub release bodies are Markdown with hard-wrapped lines; collapse every run of
        // whitespace so the balloon shows one readable paragraph.
        var collapsed = string.Join(' ', notes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (collapsed.Length == 0) return "A new version is available.";
        if (collapsed.Length <= MaxNotesLength) return collapsed;

        return collapsed[..MaxNotesLength] + "\u2026";
    }
}
