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

    public string ProviderName => _session.ProviderName;
    public string HintText { get; }

    public bool IsLoggedIn => _session.HasSession;
    public string StatusText => IsLoggedIn ? "Status: Logged In ✔️" : "Status: Not Logged In";
    public string StatusColor => IsLoggedIn ? "#2ECC71" : "#E74C3C";
    public string ButtonText => IsLoggedIn ? "Log out" : $"Log into {ProviderName}";

    public AccountRowViewModel(IProviderSession session, Func<Window> authWindowFactory, string hintText)
    {
        _session = session;
        _authWindowFactory = authWindowFactory;
        HintText = hintText;
    }

    [RelayCommand]
    private void Action()
    {
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
    }
}
