using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AiMeter.Managers;
using AiMeter.Services.Updates;
using AiMeter.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class UpdateManagerTests
{
    private static AppVersion NewerVersion()
    {
        var current = AppInfo.Current;
        return new AppVersion(current.Major, current.Minor, current.Patch + 1);
    }

    private static AppVersion OlderVersion()
    {
        var current = AppInfo.Current;
        if (current.Patch > 0) return new AppVersion(current.Major, current.Minor, current.Patch - 1);
        if (current.Minor > 0) return new AppVersion(current.Major, current.Minor - 1, 0);
        return new AppVersion(Math.Max(0, current.Major - 1), 0, 0);
    }

    private static ReleaseInfo ReleaseFor(
        AppVersion version,
        string? sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef") => new(
        version,
        "v" + version,
        $"https://github.com/LanternOfDarkness/AiMeter/releases/tag/v{version}",
        "Release notes",
        new ReleaseAsset(
            "AiMeter.exe",
            new Uri($"https://github.com/LanternOfDarkness/AiMeter/releases/download/v{version}/AiMeter.exe"),
            1024),
        sha256,
        null);

    private sealed class Harness
    {
        public Harness(UpdateState? state = null)
        {
            if (state is not null) Store.State = state;

            Manager = new UpdateManager(
                Feed,
                Downloader,
                Installer,
                Store,
                Settings,
                Time);
            Manager.UpdateAvailable += (_, release) => Notified.Add(release);
        }

        public FakeReleaseFeed Feed { get; } = new();

        public FakeUpdateDownloader Downloader { get; } = new();

        public FakeUpdateInstaller Installer { get; } = new();

        public FakeUpdateStateStore Store { get; } = new();

        public FakeSettingsManager Settings { get; } = new();

        public FakeTimeProvider Time { get; } = new();

        public UpdateManager Manager { get; }

        public List<ReleaseInfo> Notified { get; } = new();
    }

    [Fact]
    public async Task Check_never_downloads_or_installs()
    {
        var harness = new Harness();
        harness.Feed.Result = ReleaseFor(NewerVersion());

        var result = await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        result.Status.Should().Be(UpdateCheckStatus.UpdateAvailable);
        harness.Downloader.DownloadCallCount.Should().Be(0);
        harness.Installer.AppliedPaths.Should().BeEmpty();
        harness.Installer.CheckCallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Newer_version_raises_update_available()
    {
        var harness = new Harness();
        var release = ReleaseFor(NewerVersion());
        harness.Feed.Result = release;

        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        harness.Notified.Should().ContainSingle().Which.Should().Be(release);
        harness.Manager.AvailableRelease.Should().Be(release);
        harness.Manager.HasAvailableUpdate.Should().BeTrue();
        harness.Manager.IsSkipped.Should().BeFalse();
        harness.Manager.StatusText.Should().Be($"Update available — v{release.Version}");
    }

    [Fact]
    public async Task Skipped_version_is_suppressed_but_retained()
    {
        var harness = new Harness();
        var release = ReleaseFor(NewerVersion());
        harness.Feed.Result = release;
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        harness.Manager.SkipAvailableVersion();

        harness.Manager.IsSkipped.Should().BeTrue();
        harness.Manager.HasAvailableUpdate.Should().BeFalse();
        harness.Manager.HasNewerRelease.Should().BeTrue();
        harness.Manager.AvailableRelease.Should().Be(release);
        harness.Manager.StatusText.Should().Be($"Skipped — v{release.Version}");
        harness.Store.State.SkippedVersion.Should().Be(release.Version.ToString());
        harness.Store.SaveCallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Manual_check_resurfaces_skipped_version_without_notifying()
    {
        var harness = new Harness();
        var release = ReleaseFor(NewerVersion());
        harness.Feed.Result = release;
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);
        harness.Manager.SkipAvailableVersion();
        harness.Notified.Should().ContainSingle();

        harness.Time.Advance(TimeSpan.FromSeconds(11));
        var result = await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        result.Status.Should().Be(UpdateCheckStatus.Skipped);
        result.Release.Should().Be(release);
        harness.Manager.IsSkipped.Should().BeTrue();
        harness.Manager.HasAvailableUpdate.Should().BeFalse();
        harness.Notified.Should().ContainSingle();
    }

    [Fact]
    public async Task Notification_is_deduped_for_24_hours()
    {
        var harness = new Harness();
        harness.Feed.Result = ReleaseFor(NewerVersion());

        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);
        harness.Notified.Should().ContainSingle();

        harness.Time.Advance(TimeSpan.FromHours(1));
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);
        harness.Notified.Should().ContainSingle();

        harness.Time.Advance(TimeSpan.FromHours(23));
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);
        harness.Notified.Should().HaveCount(2);
    }

    [Fact]
    public async Task DismissNotification_restarts_the_24_hour_window()
    {
        var harness = new Harness();
        harness.Feed.Result = ReleaseFor(NewerVersion());

        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);
        harness.Notified.Should().ContainSingle();

        harness.Time.Advance(TimeSpan.FromHours(23));
        harness.Manager.DismissNotification();

        harness.Time.Advance(TimeSpan.FromHours(11));
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);
        harness.Notified.Should().ContainSingle();

        harness.Time.Advance(TimeSpan.FromHours(14));
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);
        harness.Notified.Should().HaveCount(2);
    }

    [Fact]
    public async Task Manual_checks_are_debounced_for_10_seconds()
    {
        var harness = new Harness();
        harness.Feed.Result = ReleaseFor(NewerVersion());

        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);
        harness.Feed.CallCount.Should().Be(1);

        harness.Time.Advance(TimeSpan.FromSeconds(11));
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);
        harness.Feed.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task No_check_runs_while_downloading()
    {
        var harness = new Harness();
        harness.Feed.Result = ReleaseFor(NewerVersion());
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        harness.Downloader.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Downloader.ProgressToReport = new DownloadProgress(50, 100, 0.5);

        var downloadTask = harness.Manager.DownloadAndApplyAsync(CancellationToken.None);
        await harness.Downloader.WaitForStartAsync();

        harness.Manager.IsDownloading.Should().BeTrue();
        harness.Manager.DownloadFraction.Should().Be(0.5);

        var callsBefore = harness.Feed.CallCount;
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);
        harness.Feed.CallCount.Should().Be(callsBefore);

        harness.Downloader.Gate.SetResult(true);
        var result = await downloadTask;

        result.RestartStarted.Should().BeTrue();
        harness.Manager.IsDownloading.Should().BeFalse();
        harness.Manager.DownloadFraction.Should().BeNull();
    }

    [Fact]
    public async Task LastCheckUtc_is_set_only_after_success()
    {
        var harness = new Harness();
        harness.Feed.Exception = new ReleaseFeedException("boom");

        var result = await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        result.Status.Should().Be(UpdateCheckStatus.Failed);
        harness.Manager.LastCheckUtc.Should().BeNull();
        harness.Manager.LastError.Should().Be("boom");
        harness.Manager.StatusText.Should().Be("Check failed — try again later");
        harness.Store.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Preflight_failure_blocks_the_download()
    {
        var harness = new Harness();
        harness.Installer.Preflight = new UpdatePreflight(false, "development build");
        harness.Feed.Result = ReleaseFor(NewerVersion());
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        var result = await harness.Manager.DownloadAndApplyAsync(CancellationToken.None);

        result.RestartStarted.Should().BeFalse();
        result.Error.Should().Be("development build");
        harness.Downloader.DownloadCallCount.Should().Be(0);
        harness.Installer.AppliedPaths.Should().BeEmpty();
        harness.Manager.IsInstallable.Should().BeFalse();
        harness.Manager.InstallUnavailableReason.Should().Be("development build");
    }

    [Fact]
    public async Task Download_requires_the_explicit_install_call()
    {
        var harness = new Harness();
        harness.Feed.Result = ReleaseFor(NewerVersion());
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        harness.Downloader.DownloadCallCount.Should().Be(0);
        harness.Installer.AppliedPaths.Should().BeEmpty();

        var result = await harness.Manager.DownloadAndApplyAsync(CancellationToken.None);

        result.RestartStarted.Should().BeTrue();
        harness.Downloader.DownloadCallCount.Should().Be(1);
        harness.Installer.AppliedPaths.Should().ContainSingle().Which.Should().Be(harness.Downloader.StagedPath);
        harness.Manager.StatusText.Should().Be("Restarting…");
    }

    [Fact]
    public async Task Rate_limit_returns_a_status_instead_of_throwing()
    {
        var harness = new Harness();
        harness.Feed.Exception = new ReleaseFeedRateLimitedException();

        var result = await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        result.Status.Should().Be(UpdateCheckStatus.RateLimited);
        harness.Manager.LastError.Should().BeNull();
        harness.Manager.StatusText.Should().Be("GitHub rate limit reached; try again later");
        harness.Manager.LastCheckUtc.Should().BeNull();
    }

    [Fact]
    public async Task StopAsync_completes_quickly_after_start()
    {
        var harness = new Harness();
        await harness.Manager.StartAsync();
        harness.Downloader.CleanupCallCount.Should().Be(1);

        var stopwatch = Stopwatch.StartNew();
        await harness.Manager.StopAsync();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Persisted_skip_is_restored()
    {
        var release = ReleaseFor(NewerVersion());
        var harness = new Harness(new UpdateState { SkippedVersion = release.Version.ToString() });
        harness.Feed.Result = release;

        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        harness.Manager.IsSkipped.Should().BeTrue();
        harness.Manager.HasAvailableUpdate.Should().BeFalse();
        harness.Manager.AvailableRelease.Should().Be(release);
        harness.Notified.Should().BeEmpty();
    }

    [Fact]
    public async Task Installed_version_is_not_offered_or_downloaded()
    {
        var harness = new Harness();
        var release = ReleaseFor(AppInfo.Current);
        harness.Feed.Result = release;

        var result = await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        result.Status.Should().Be(UpdateCheckStatus.UpToDate);
        harness.Manager.AvailableRelease.Should().Be(release);
        harness.Manager.HasAvailableUpdate.Should().BeFalse();
        harness.Manager.IsSkipped.Should().BeFalse();
        harness.Manager.StatusText.Should().Be("You're up to date");
        harness.Manager.IsInstallable.Should().BeTrue();
        harness.Notified.Should().BeEmpty();

        var install = await harness.Manager.DownloadAndApplyAsync(CancellationToken.None);

        install.RestartStarted.Should().BeFalse();
        install.Error.Should().Be("No update available.");
        harness.Downloader.DownloadCallCount.Should().Be(0);
        harness.Installer.AppliedPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task Older_version_is_not_offered_or_downloaded()
    {
        var harness = new Harness();
        harness.Feed.Result = ReleaseFor(OlderVersion());

        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        harness.Manager.HasAvailableUpdate.Should().BeFalse();
        harness.Manager.IsSkipped.Should().BeFalse();
        harness.Manager.StatusText.Should().Be("You're up to date");
        harness.Notified.Should().BeEmpty();

        var install = await harness.Manager.DownloadAndApplyAsync(CancellationToken.None);

        install.RestartStarted.Should().BeFalse();
        harness.Downloader.DownloadCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Release_without_a_checksum_is_not_installable()
    {
        var harness = new Harness();
        harness.Feed.Result = ReleaseFor(NewerVersion(), sha256: null);

        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        harness.Manager.HasAvailableUpdate.Should().BeTrue();
        harness.Manager.IsInstallable.Should().BeFalse();
        harness.Manager.InstallUnavailableReason.Should().Contain("checksum");

        var install = await harness.Manager.DownloadAndApplyAsync(CancellationToken.None);

        install.RestartStarted.Should().BeFalse();
        install.Error.Should().Be(harness.Manager.InstallUnavailableReason);
        harness.Downloader.DownloadCallCount.Should().Be(0);
        harness.Installer.AppliedPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task Skipped_newer_version_still_installs_when_explicitly_requested()
    {
        var harness = new Harness();
        harness.Feed.Result = ReleaseFor(NewerVersion());
        await harness.Manager.CheckAsync(manual: true, CancellationToken.None);

        harness.Manager.SkipAvailableVersion();
        harness.Manager.HasAvailableUpdate.Should().BeFalse();

        var result = await harness.Manager.DownloadAndApplyAsync(CancellationToken.None);

        result.RestartStarted.Should().BeTrue();
        harness.Downloader.DownloadCallCount.Should().Be(1);
        harness.Installer.AppliedPaths.Should().ContainSingle();
    }
}
