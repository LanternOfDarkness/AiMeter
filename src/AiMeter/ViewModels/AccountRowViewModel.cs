using System;
using System.Windows;
using AiMeter.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiMeter.ViewModels;

/// <summary>
/// One row in the Settings ACCOUNTS section: a per-provider login/logout button plus status.
/// Generalizes what used to be Claude-only hardcoded properties so every provider (Claude,
/// OpenCode, …) renders from the same template. The auth window is supplied as a factory so
/// this view model stays free of the concrete window types and DI container.
/// </summary>
public partial class AccountRowViewModel : ObservableObject
{
    private readonly IProviderSession _session;
    private readonly Func<Window> _authWindowFactory;
    private readonly System.Action? _onSessionChanged;

    public string ProviderName => _session.ProviderName;
    public string HintText { get; }

    public bool IsLoggedIn => _session.HasSession;
    public string StatusText => !IsLoggedIn ? "Status: Not Logged In"
        : _session.StatusNote is { } note ? $"Status: Logged In · {note}"
        : "Status: Logged In ✔️";
    // Orange for "logged in, but something to know" (e.g. no subscription to meter).
    public string StatusColor => !IsLoggedIn ? "#E74C3C" : _session.StatusNote is null ? "#2ECC71" : "#E67E22";
    public string ButtonText => IsLoggedIn ? "Log out" : $"Log into {ProviderName}";

    public AccountRowViewModel(IProviderSession session, Func<Window> authWindowFactory, string hintText,
        System.Action? onSessionChanged = null)
    {
        _session = session;
        _authWindowFactory = authWindowFactory;
        HintText = hintText;
        _onSessionChanged = onSessionChanged;

        // The provider updates the note from its poll; refresh the status line when it does.
        _session.StatusNoteChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        };
    }

    [RelayCommand]
    private void Action()
    {
        var wasLoggedIn = IsLoggedIn;
        if (IsLoggedIn)
        {
            _session.Clear();
        }
        else
        {
            _authWindowFactory().ShowDialog();
        }

        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(ButtonText));

        if (IsLoggedIn != wasLoggedIn)
        {
            _onSessionChanged?.Invoke();
        }
    }
}
