using System.Diagnostics;
using System.Globalization;
using System.Windows;
using AiMeter.Managers;
using AiMeter.Services.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AiMeter.ViewModels;

/// <summary>
/// Mirrors <see cref="IUpdateManager"/> state for the update window and Settings, and forwards
/// the user's consent actions. All events are marshalled to the UI thread before refresh.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateManager _manager;
    private readonly IUiDispatcher _dispatcher;
    private readonly IProcessLauncher _launcher;
    private readonly ILogger<UpdateViewModel>? _logger;
    private CancellationTokenSource? _downloadCts;

    public UpdateViewModel(
        IUpdateManager manager,
        IUiDispatcher dispatcher,
        IProcessLauncher launcher,
        ILogger<UpdateViewModel>? logger = null)
    {
        _manager = manager;
        _dispatcher = dispatcher;
        _launcher = launcher;
        _logger = logger;

        _manager.StateChanged += OnManagerStateChanged;
    }

    public AppVersion CurrentVersion => _manager.CurrentVersion;

    public string CurrentVersionText => AppInfo.CurrentDisplay;

    public string? NewVersionText =>
        _manager.AvailableRelease is { } release ? "v" + release.Version : null;

    public string VersionTransitionText =>
        _manager.AvailableRelease is { } release && _manager.HasAvailableUpdate
            ? $"{CurrentVersionText} \u2192 v{release.Version}"
            : CurrentVersionText;

    public string StatusText => _manager.StatusText;

    public string Notes => _manager.AvailableRelease?.Notes ?? string.Empty;

    public string? ReleasePageUrl => _manager.AvailableRelease?.HtmlUrl;

    public bool HasAvailableUpdate => _manager.HasAvailableUpdate;

    public bool IsSkipped => _manager.IsSkipped;

    public bool IsInstallable => _manager.IsInstallable;

    public bool IsChecking => _manager.IsChecking;

    public bool IsDownloading => _manager.IsDownloading;

    public double? DownloadFraction => _manager.DownloadFraction;

    public bool IsProgressDeterminate => DownloadFraction is not null;

    public string? InstallUnavailableReason => _manager.InstallUnavailableReason;

    public string? LastError => _manager.LastError;

    public string LastCheckedText =>
        _manager.LastCheckUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
        ?? "Never";

    public bool ShowDownloadButton =>
        _manager.AvailableRelease is not null && _manager.IsInstallable && !_manager.IsDownloading;

    public bool ShowReleasePageButton =>
        _manager.AvailableRelease is not null && !_manager.IsInstallable && !_manager.IsDownloading;

    public bool ShowCancelButton => _manager.IsDownloading;

    public bool ShowRetryButton =>
        _manager.LastError is not null
        && _manager.AvailableRelease is not null
        && _manager.IsInstallable
        && !_manager.IsDownloading;

    public bool ShowSkipButton => _manager.HasAvailableUpdate && !_manager.IsDownloading;

    public bool CanCheckNow => !_manager.IsChecking && !_manager.IsDownloading;

    /// <summary>Runs the first check when the update window opens before any check completed.</summary>
    public void EnsureFirstCheck()
    {
        if (_manager.LastCheckUtc is not null) return;

        // Fire-and-forget on purpose: the window must open immediately. Failures are already
        // mapped to StatusText by the manager; this only guards against an unexpected throw.
        _ = CheckFirstTimeAsync();
    }

    private async Task CheckFirstTimeAsync()
    {
        try
        {
            await CheckNowAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Initial update check failed.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNowAsync()
    {
        await _manager.CheckAsync(manual: true, CancellationToken.None);
    }

    [RelayCommand]
    private async Task DownloadAndInstallAsync()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _downloadCts, cts);
        previous?.Dispose();

        try
        {
            var result = await _manager.DownloadAndApplyAsync(cts.Token);
            if (result.RestartStarted)
            {
                Application.Current?.Shutdown();
            }
        }
        catch (OperationCanceledException)
        {
            // The user pressed Cancel; the manager has already cleaned up and reset its state.
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _downloadCts, null, cts) == cts)
            {
                cts.Dispose();
            }
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        try
        {
            _downloadCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The download already finished between the click and the cancellation.
        }
    }

    [RelayCommand]
    private void Later()
    {
        try
        {
            _manager.DismissNotification();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not dismiss the update notification.");
        }
    }

    [RelayCommand]
    private void SkipVersion()
    {
        try
        {
            _manager.SkipAvailableVersion();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not skip the available update.");
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (ReleasePageUrl is not { } url) return;

        try
        {
            _launcher.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not open the release page.");
        }
    }

    private void OnManagerStateChanged(object? sender, EventArgs e)
    {
        _dispatcher.Post(Refresh);
    }

    private void Refresh()
    {
        OnPropertyChanged((string?)null);
        CheckNowCommand.NotifyCanExecuteChanged();
    }
}
