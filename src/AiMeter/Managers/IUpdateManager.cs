using System;
using System.Threading;
using System.Threading.Tasks;
using AiMeter.Services.Updates;

namespace AiMeter.Managers;

/// <summary>Outcome of one update check.</summary>
public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Skipped,
    RateLimited,
    Failed,
}

/// <summary>Result of <see cref="IUpdateManager.CheckAsync"/>.</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release, string? Error);

/// <summary>
/// Drives the update lifecycle: checks for releases, tracks notification state, and performs
/// the user-approved download and in-place install. Has no WPF dependency.
/// </summary>
public interface IUpdateManager
{
    AppVersion CurrentVersion { get; }

    /// <summary>The newest release from the last check, even when it is skipped or older.</summary>
    ReleaseInfo? AvailableRelease { get; }

    /// <summary>True when a newer release exists and is not skipped.</summary>
    bool HasAvailableUpdate { get; }

    /// <summary>True when <see cref="AvailableRelease"/> is the version the user skipped.</summary>
    bool IsSkipped { get; }

    /// <summary>True when this instance can replace its own executable.</summary>
    bool IsInstallable { get; }

    /// <summary>Why automatic installation is unavailable, or <see langword="null"/> when it is.</summary>
    string? InstallUnavailableReason { get; }

    bool IsChecking { get; }

    bool IsDownloading { get; }

    /// <summary>Downloaded fraction in [0, 1], or <see langword="null"/> when unknown.</summary>
    double? DownloadFraction { get; }

    DateTimeOffset? LastCheckUtc { get; }

    string StatusText { get; }

    string? LastError { get; }

    event EventHandler? StateChanged;

    /// <summary>Raised at most once per version per 24 hours.</summary>
    event EventHandler<ReleaseInfo>? UpdateAvailable;

    Task StartAsync();

    Task StopAsync();

    Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken ct);

    /// <summary>
    /// Downloads, verifies, installs and relaunches the available update. Consent is assumed;
    /// <see cref="CheckAsync"/> never downloads anything.
    /// </summary>
    Task<InstallResult> DownloadAndApplyAsync(CancellationToken ct);

    /// <summary>Suppresses notifications for 24 hours (the tray "Later" action).</summary>
    void DismissNotification();

    /// <summary>Suppresses notifications for the available version; the release stays downloadable.</summary>
    void SkipAvailableVersion();
}
