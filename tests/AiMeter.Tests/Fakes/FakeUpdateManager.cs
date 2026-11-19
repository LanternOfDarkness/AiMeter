using AiMeter.Managers;
using AiMeter.Services.Updates;

namespace AiMeter.Tests.Fakes;

/// <summary>Configurable <see cref="IUpdateManager"/> that records every user action.</summary>
public sealed class FakeUpdateManager : IUpdateManager
{
    public AppVersion CurrentVersion { get; set; } = new(2, 0, 2);

    public ReleaseInfo? AvailableRelease { get; set; }

    public bool HasAvailableUpdate { get; set; }

    public bool IsSkipped { get; set; }

    public bool IsInstallable { get; set; } = true;

    public string? InstallUnavailableReason { get; set; }

    public bool IsChecking { get; set; }

    public bool IsDownloading { get; set; }

    public double? DownloadFraction { get; set; }

    public DateTimeOffset? LastCheckUtc { get; set; }

    public string StatusText { get; set; } = string.Empty;

    public string? LastError { get; set; }

    public event EventHandler? StateChanged;

    public event EventHandler<ReleaseInfo>? UpdateAvailable;

    public int CheckCallCount { get; private set; }

    public bool? LastCheckManual { get; private set; }

    public CancellationToken LastCheckToken { get; private set; }

    public int DownloadCallCount { get; private set; }

    public CancellationToken LastDownloadToken { get; private set; }

    public InstallResult DownloadResult { get; set; } = new(false, null);

    public Func<CancellationToken, Task<InstallResult>>? DownloadHandler { get; set; }

    public int DismissCallCount { get; private set; }

    public int SkipCallCount { get; private set; }

    public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void RaiseUpdateAvailable(ReleaseInfo release) => UpdateAvailable?.Invoke(this, release);

    public Task StartAsync() => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken ct)
    {
        CheckCallCount++;
        LastCheckManual = manual;
        LastCheckToken = ct;
        return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.UpToDate, AvailableRelease, null));
    }

    public Task<InstallResult> DownloadAndApplyAsync(CancellationToken ct)
    {
        DownloadCallCount++;
        LastDownloadToken = ct;
        return DownloadHandler?.Invoke(ct) ?? Task.FromResult(DownloadResult);
    }

    public void DismissNotification() => DismissCallCount++;

    public void SkipAvailableVersion() => SkipCallCount++;
}
