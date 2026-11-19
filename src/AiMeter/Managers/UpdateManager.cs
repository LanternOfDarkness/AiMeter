using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AiMeter.Services.Updates;
using Microsoft.Extensions.Logging;

namespace AiMeter.Managers;

/// <summary>
/// Coordinates update checks, notification dedupe and the user-approved install. All time
/// decisions go through the injected <see cref="TimeProvider"/>; no WPF types are touched.
/// </summary>
public sealed class UpdateManager : IUpdateManager
{
    private static readonly TimeSpan ManualDebounce = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NotificationWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CheckAgeLimit = TimeSpan.FromHours(24);

    private readonly IReleaseFeed _feed;
    private readonly IUpdateDownloader _downloader;
    private readonly IUpdateInstaller _installer;
    private readonly IUpdateStateStore _stateStore;
    private readonly ISettingsManager _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateManager>? _logger;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly object _gate = new();

    private UpdateState _state;
    private UpdatePreflight _preflight = new(false, null);
    private Task<UpdateCheckResult>? _inFlight;
    private UpdateCheckResult? _lastResult;
    private DateTimeOffset? _lastManualCheckUtc;
    private DateTimeOffset? _nextAttemptUtc;
    private CancellationTokenSource? _loopCts;
    private CancellationTokenSource? _downloadCts;
    private Task? _loopTask;
    private bool _isChecking;
    private bool _isDownloading;
    private double? _downloadFraction;
    private string _statusText = "Not checked yet";
    private string? _lastError;

    /// <summary>Creates the manager and restores persisted skip/notification state.</summary>
    public UpdateManager(
        IReleaseFeed feed,
        IUpdateDownloader downloader,
        IUpdateInstaller installer,
        IUpdateStateStore stateStore,
        ISettingsManager settings,
        TimeProvider timeProvider,
        ILogger<UpdateManager>? logger = null)
    {
        _feed = feed;
        _downloader = downloader;
        _installer = installer;
        _stateStore = stateStore;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;

        try
        {
            _state = stateStore.Load() ?? new UpdateState();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not load the update state; starting fresh.");
            _state = new UpdateState();
        }
    }

    /// <inheritdoc />
    public AppVersion CurrentVersion => AppInfo.Current;

    /// <inheritdoc />
    public ReleaseInfo? AvailableRelease { get; private set; }

    /// <inheritdoc />
    public bool HasAvailableUpdate => OffersNewerVersion && !IsSkipped;

    private bool OffersNewerVersion =>
        AvailableRelease is { } release && release.Version.CompareTo(AppInfo.Current) > 0;

    /// <inheritdoc />
    public bool IsSkipped
    {
        get
        {
            if (AvailableRelease is not { } release) return false;
            if (string.IsNullOrWhiteSpace(_state.SkippedVersion)) return false;

            return AppVersion.TryParse(_state.SkippedVersion, out var skipped)
                && skipped.CompareTo(release.Version) == 0;
        }
    }

    /// <inheritdoc />
    public bool IsInstallable => _preflight.CanUpdateInPlace && HasUsableChecksum(AvailableRelease);

    /// <inheritdoc />
    public string? InstallUnavailableReason =>
        _preflight.CanUpdateInPlace
            ? HasUsableChecksum(AvailableRelease)
                ? null
                : "This release has no checksum; install it from the release page."
            : _preflight.Reason;

    /// <inheritdoc />
    public bool IsChecking => _isChecking;

    /// <inheritdoc />
    public bool IsDownloading => _isDownloading;

    /// <inheritdoc />
    public double? DownloadFraction => _downloadFraction;

    /// <inheritdoc />
    public DateTimeOffset? LastCheckUtc => _state.LastCheckUtc;

    /// <inheritdoc />
    public string StatusText => _statusText;

    /// <inheritdoc />
    public string? LastError => _lastError;

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public event EventHandler<ReleaseInfo>? UpdateAvailable;

    /// <inheritdoc />
    public Task StartAsync()
    {
        if (_loopCts is not null) return Task.CompletedTask;

        try
        {
            var removed = _downloader.CleanupStagedFiles();
            if (removed > 0)
            {
                _logger?.LogInformation("Removed {Count} stale staged update files.", removed);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not clean up staged update files.");
        }

        RefreshPreflight();

        _loopCts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_loopCts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        var loopCts = _loopCts;
        var downloadCts = _downloadCts;
        var loopTask = _loopTask;

        try
        {
            loopCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            downloadCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (loopTask is not null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "The update loop ended with an error.");
            }
        }

        _loopCts = null;
        _loopTask = null;
    }

    /// <inheritdoc />
    public Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken ct)
    {
        if (IsDownloading)
        {
            return Task.FromResult(_lastResult ?? new UpdateCheckResult(UpdateCheckStatus.UpToDate, AvailableRelease, null));
        }

        var now = _timeProvider.GetUtcNow();
        if (manual
            && _lastManualCheckUtc is { } lastManual
            && now - lastManual < ManualDebounce
            && _lastResult is { } cached)
        {
            return Task.FromResult(cached);
        }

        if (!manual && !_settings.Current.CheckForUpdatesAutomatically)
        {
            return Task.FromResult(_lastResult ?? new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, null));
        }

        Task<UpdateCheckResult> task;
        lock (_gate)
        {
            task = _inFlight ??= RunCheckAsync(manual, ct);
        }

        return CompleteCheckAsync(task);
    }

    /// <inheritdoc />
    public async Task<InstallResult> DownloadAndApplyAsync(CancellationToken ct)
    {
        if (AvailableRelease is not { } release || !OffersNewerVersion)
        {
            return new InstallResult(false, "No update available.");
        }

        RefreshPreflight();
        if (!_preflight.CanUpdateInPlace)
        {
            return new InstallResult(false, _preflight.Reason ?? "Automatic update unavailable.");
        }

        if (!HasUsableChecksum(release))
        {
            return new InstallResult(
                false,
                InstallUnavailableReason ?? "This release has no checksum; install it from the release page.");
        }

        var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _downloadCts = downloadCts;
        _isDownloading = true;
        _downloadFraction = null;
        _lastError = null;
        _statusText = "Downloading…";
        RaiseStateChanged();

        try
        {
            var progress = new InlineProgress<DownloadProgress>(p =>
            {
                _downloadFraction = p.Fraction;
                _statusText = p.Fraction is { } fraction
                    ? $"Downloading {Math.Round(fraction * 100)}%"
                    : "Downloading…";
                RaiseStateChanged();
            });

            var stagedPath = await _downloader
                .DownloadAsync(release, GetDefaultStagingDirectory(), progress, downloadCts.Token)
                .ConfigureAwait(false);

            _statusText = "Verifying…";
            RaiseStateChanged();

            _statusText = "Installing…";
            RaiseStateChanged();

            var result = await _installer.ApplyAndRestartAsync(stagedPath, downloadCts.Token).ConfigureAwait(false);

            if (result.RestartStarted)
            {
                _statusText = "Restarting…";
            }
            else
            {
                _lastError = result.Error;
                _statusText = "Update failed";
                _logger?.LogWarning("The update could not be installed: {Error}", result.Error);
            }

            RaiseStateChanged();
            return result;
        }
        catch (OperationCanceledException)
        {
            _lastError = null;
            _statusText = GetAvailableStatus(release);
            return new InstallResult(false, null);
        }
        catch (UpdateIntegrityException ex)
        {
            _lastError = ex.Message;
            _statusText = "Download failed";
            _logger?.LogWarning(ex, "The downloaded update failed verification.");
            return new InstallResult(false, ex.Message);
        }
        catch (UpdateDownloadException ex)
        {
            _lastError = ex.Message;
            _statusText = "Download failed";
            _logger?.LogWarning(ex, "The update download failed.");
            return new InstallResult(false, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _lastError = ex.Message;
            _statusText = "Download failed";
            _logger?.LogWarning(ex, "The update download or staging failed.");
            return new InstallResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _statusText = "Update failed";
            _logger?.LogError(ex, "The update install failed.");
            return new InstallResult(false, ex.Message);
        }
        finally
        {
            _isDownloading = false;
            _downloadFraction = null;
            _downloadCts = null;
            downloadCts.Dispose();
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    public void DismissNotification()
    {
        _state.LastNotifiedUtc = _timeProvider.GetUtcNow();
        PersistState();
        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void SkipAvailableVersion()
    {
        if (AvailableRelease is not { } release) return;

        _state.SkippedVersion = release.Version.ToString();
        _statusText = $"Skipped — v{release.Version}";
        PersistState();
        RaiseStateChanged();
    }

    private async Task<UpdateCheckResult> CompleteCheckAsync(Task<UpdateCheckResult> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_inFlight, task)) _inFlight = null;
            }
        }
    }

    private async Task<UpdateCheckResult> RunCheckAsync(bool manual, CancellationToken ct)
    {
        await Task.Yield();

        try
        {
            await _checkLock.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return _lastResult
                ?? new UpdateCheckResult(UpdateCheckStatus.Failed, AvailableRelease, "The update check was cancelled.");
        }

        _isChecking = true;
        RaiseStateChanged();

        try
        {
            var now = _timeProvider.GetUtcNow();
            if (manual) _lastManualCheckUtc = now;

            ReleaseInfo? release;
            try
            {
                release = await _feed.GetLatestAsync(ct).ConfigureAwait(false);
            }
            catch (ReleaseFeedRateLimitedException)
            {
                _nextAttemptUtc = now + RetryDelay;
                _lastError = null;
                _statusText = "GitHub rate limit reached; try again later";
                _logger?.LogInformation("The GitHub release check was rate limited.");
                return Remember(new UpdateCheckResult(UpdateCheckStatus.RateLimited, null, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Remember(_lastResult
                    ?? new UpdateCheckResult(UpdateCheckStatus.Failed, AvailableRelease, null));
            }
            catch (Exception ex)
            {
                _nextAttemptUtc = now + RetryDelay;
                _lastError = ex.Message;
                _statusText = "Check failed — try again later";
                _logger?.LogWarning(ex, "The update check failed.");
                return Remember(new UpdateCheckResult(UpdateCheckStatus.Failed, null, ex.Message));
            }

            return Remember(ApplyCheckResult(release, now));
        }
        finally
        {
            _isChecking = false;
            _checkLock.Release();
            RaiseStateChanged();
        }
    }

    private UpdateCheckResult ApplyCheckResult(ReleaseInfo? release, DateTimeOffset now)
    {
        _lastError = null;
        _nextAttemptUtc = null;
        AvailableRelease = release;
        _state.LastCheckUtc = now;

        if (release is null)
        {
            _statusText = "You're up to date";
            PersistState();
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, null);
        }

        RefreshPreflight();
        PersistState();

        var version = release.Version;
        var isNewer = version.CompareTo(AppInfo.Current) > 0;
        var skipped = IsSkipped;

        _statusText = skipped
            ? $"Skipped — v{version}"
            : isNewer
                ? $"Update available — v{version}"
                : "You're up to date";

        if (isNewer && !skipped && ShouldNotify(release, now))
        {
            _state.LastNotifiedVersion = version.ToString();
            _state.LastNotifiedUtc = now;
            PersistState();
            RaiseUpdateAvailable(release);
        }

        var status = skipped
            ? UpdateCheckStatus.Skipped
            : isNewer
                ? UpdateCheckStatus.UpdateAvailable
                : UpdateCheckStatus.UpToDate;

        return new UpdateCheckResult(status, release, null);
    }

    private bool ShouldNotify(ReleaseInfo release, DateTimeOffset now)
    {
        if (!string.Equals(_state.LastNotifiedVersion, release.Version.ToString(), StringComparison.Ordinal))
        {
            return true;
        }

        return _state.LastNotifiedUtc is not { } lastNotified || now - lastNotified >= NotificationWindow;
    }

    private UpdateCheckResult Remember(UpdateCheckResult result)
    {
        _lastResult = result;
        return result;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, _timeProvider, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                if (ShouldRunAutomaticCheck(_timeProvider.GetUtcNow()))
                {
                    await CheckAsync(manual: false, ct).ConfigureAwait(false);
                }

                await Task.Delay(CheckInterval, _timeProvider, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "The automatic update loop stopped unexpectedly.");
        }
    }

    private bool ShouldRunAutomaticCheck(DateTimeOffset now)
    {
        if (_isDownloading) return false;
        if (!_settings.Current.CheckForUpdatesAutomatically) return false;
        if (_nextAttemptUtc is { } nextAttempt && nextAttempt > now) return false;
        if (_state.LastCheckUtc is { } lastCheck && now - lastCheck < CheckAgeLimit) return false;

        return true;
    }

    private void RefreshPreflight()
    {
        try
        {
            _preflight = _installer.CheckCanUpdateInPlace();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "The update preflight check failed; automatic install is disabled.");
            _preflight = new UpdatePreflight(false, "Automatic update unavailable.");
        }
    }

    private static bool HasUsableChecksum(ReleaseInfo? release) =>
        release is null || release.Sha256 is not null || release.ChecksumAsset is not null;

    private string GetAvailableStatus(ReleaseInfo release)
    {
        if (IsSkipped) return $"Skipped — v{release.Version}";

        return release.Version.CompareTo(AppInfo.Current) > 0
            ? $"Update available — v{release.Version}"
            : "You're up to date";
    }

    private void PersistState()
    {
        try
        {
            _stateStore.Save(_state);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not persist the update state.");
        }
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "An update state handler threw.");
        }
    }

    private void RaiseUpdateAvailable(ReleaseInfo release)
    {
        try
        {
            UpdateAvailable?.Invoke(this, release);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "An update notification handler threw.");
        }
    }

    private static string GetDefaultStagingDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiMeter",
        "updates");

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(Action<T> report) => _report = report;

        public void Report(T value) => _report(value);
    }
}
