using System.Diagnostics;
using System.Globalization;
using AiMeter.Services.Updates;
using AiMeter.Tests.Fakes;
using AiMeter.ViewModels;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class UpdateViewModelTests
{
    private static ReleaseInfo Release() => new(
        new AppVersion(9, 9, 9),
        "v9.9.9",
        "https://github.com/LanternOfDarkness/AiMeter/releases/tag/v9.9.9",
        "Release notes",
        new ReleaseAsset(
            "AiMeter.exe",
            new Uri("https://github.com/LanternOfDarkness/AiMeter/releases/download/v9.9.9/AiMeter.exe"),
            1024),
        null,
        null);

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        public List<ProcessStartInfo> StartInfos { get; } = new();

        public Process Start(ProcessStartInfo startInfo)
        {
            StartInfos.Add(startInfo);
            return new Process();
        }
    }

    private static (UpdateViewModel ViewModel, FakeUpdateManager Manager, FakeProcessLauncher Launcher) Create()
    {
        var manager = new FakeUpdateManager();
        var launcher = new FakeProcessLauncher();
        var viewModel = new UpdateViewModel(manager, new FakeUiDispatcher(), launcher);
        return (viewModel, manager, launcher);
    }

    [Fact]
    public void MirrorsManagerStateAfterStateChanged()
    {
        var (vm, manager, _) = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var lastCheck = new DateTimeOffset(2026, 11, 19, 14, 2, 0, TimeSpan.FromHours(2));
        var release = Release();
        manager.CurrentVersion = new AppVersion(2, 0, 2);
        manager.AvailableRelease = release;
        manager.HasAvailableUpdate = true;
        manager.IsSkipped = true;
        manager.IsInstallable = false;
        manager.InstallUnavailableReason = "Development build";
        manager.IsChecking = true;
        manager.IsDownloading = false;
        manager.DownloadFraction = 0.42;
        manager.LastCheckUtc = lastCheck;
        manager.StatusText = "Update available";
        manager.LastError = "Verification failed";

        manager.RaiseStateChanged();

        changed.Should().Contain((string?)null);
        vm.CurrentVersion.Should().Be(new AppVersion(2, 0, 2));
        vm.CurrentVersionText.Should().Be(AppInfo.CurrentDisplay);
        vm.NewVersionText.Should().Be("v9.9.9");
        // Skipped releases still keep the transition so the window can show what is on offer.
        vm.VersionTransitionText.Should().Be($"{AppInfo.CurrentDisplay} \u2192 v9.9.9");
        vm.StatusText.Should().Be("Update available");
        vm.Notes.Should().Be("Release notes");
        vm.ReleasePageUrl.Should().Be(release.HtmlUrl);
        vm.HasAvailableUpdate.Should().BeTrue();
        vm.IsSkipped.Should().BeTrue();
        vm.IsInstallable.Should().BeFalse();
        vm.IsChecking.Should().BeTrue();
        vm.IsDownloading.Should().BeFalse();
        vm.DownloadFraction.Should().Be(0.42);
        vm.IsProgressDeterminate.Should().BeTrue();
        vm.InstallUnavailableReason.Should().Be("Development build");
        vm.LastError.Should().Be("Verification failed");
        vm.LastCheckedText.Should().Be(
            lastCheck.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));

        vm.ShowDownloadButton.Should().BeFalse();
        vm.ShowReleasePageButton.Should().BeTrue();
        vm.ShowCancelButton.Should().BeFalse();
        vm.ShowRetryButton.Should().BeFalse();
        vm.ShowSkipButton.Should().BeTrue();
        vm.CanCheckNow.Should().BeFalse();

        manager.HasAvailableUpdate = false;
        manager.RaiseStateChanged();
        vm.VersionTransitionText.Should().Be(AppInfo.CurrentDisplay);
    }

    [Fact]
    public void ShowsDownloadAndSkipButtonsWhenInstallable()
    {
        var (vm, manager, _) = Create();
        manager.AvailableRelease = Release();
        manager.HasAvailableUpdate = true;

        manager.RaiseStateChanged();

        vm.ShowDownloadButton.Should().BeTrue();
        vm.ShowReleasePageButton.Should().BeFalse();
        vm.ShowRetryButton.Should().BeFalse();
        vm.ShowSkipButton.Should().BeTrue();
        vm.CanCheckNow.Should().BeTrue();
    }

    [Fact]
    public async Task CheckNowCommandCallsManualCheckOnceAndIsDisabledWhileDownloading()
    {
        var (vm, manager, _) = Create();

        await vm.CheckNowCommand.ExecuteAsync(null);

        manager.CheckCallCount.Should().Be(1);
        manager.LastCheckManual.Should().BeTrue();
        manager.LastCheckToken.Should().Be(CancellationToken.None);
        vm.CheckNowCommand.CanExecute(null).Should().BeTrue();

        manager.IsDownloading = true;
        manager.RaiseStateChanged();
        vm.CheckNowCommand.CanExecute(null).Should().BeFalse();

        manager.IsDownloading = false;
        manager.IsChecking = true;
        manager.RaiseStateChanged();
        vm.CheckNowCommand.CanExecute(null).Should().BeFalse();

        manager.IsChecking = false;
        manager.RaiseStateChanged();
        vm.CheckNowCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task DownloadAndInstallCommandCallsManagerOnce()
    {
        var (vm, manager, _) = Create();
        manager.DownloadResult = new InstallResult(false, null);

        await vm.DownloadAndInstallCommand.ExecuteAsync(null);

        manager.DownloadCallCount.Should().Be(1);
        manager.LastDownloadToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task CancelCommandCancelsTheDownloadToken()
    {
        var (vm, manager, _) = Create();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.DownloadHandler = async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new InstallResult(false, null);
        };

        var run = vm.DownloadAndInstallCommand.ExecuteAsync(null);
        await started.Task;

        vm.CancelCommand.Execute(null);
        await run;

        manager.LastDownloadToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void LaterAndSkipCommandsDelegateToManager()
    {
        var (vm, manager, _) = Create();

        vm.LaterCommand.Execute(null);
        vm.SkipVersionCommand.Execute(null);

        manager.DismissCallCount.Should().Be(1);
        manager.SkipCallCount.Should().Be(1);
    }

    [Fact]
    public void OpenReleasePageUsesShellExecute()
    {
        var (vm, manager, launcher) = Create();
        var release = Release();
        manager.AvailableRelease = release;

        vm.OpenReleasePageCommand.Execute(null);

        launcher.StartInfos.Should().ContainSingle();
        launcher.StartInfos[0].FileName.Should().Be(release.HtmlUrl);
        launcher.StartInfos[0].UseShellExecute.Should().BeTrue();
    }

    [Fact]
    public void OpenReleasePageIsNoOpWithoutRelease()
    {
        var (vm, manager, launcher) = Create();
        manager.AvailableRelease = null;

        var act = () => vm.OpenReleasePageCommand.Execute(null);

        act.Should().NotThrow();
        launcher.StartInfos.Should().BeEmpty();
    }

    [Fact]
    public void LastCheckedTextIsNeverWithoutACheck()
    {
        var (vm, manager, _) = Create();
        manager.LastCheckUtc = null;

        manager.RaiseStateChanged();

        vm.LastCheckedText.Should().Be("Never");
    }

    [Fact]
    public void IsProgressDeterminateFollowsDownloadFraction()
    {
        var (vm, manager, _) = Create();
        manager.DownloadFraction = 0.25;
        manager.RaiseStateChanged();

        vm.IsProgressDeterminate.Should().BeTrue();
        vm.DownloadFraction.Should().Be(0.25);

        manager.DownloadFraction = null;
        manager.RaiseStateChanged();

        vm.IsProgressDeterminate.Should().BeFalse();
        vm.DownloadFraction.Should().BeNull();
    }

    [Fact]
    public void EnsureFirstCheckOnlyRunsBeforeTheFirstCompletedCheck()
    {
        var (vm, manager, _) = Create();
        manager.LastCheckUtc = null;

        vm.EnsureFirstCheck();

        manager.CheckCallCount.Should().Be(1);
        manager.LastCheckManual.Should().BeTrue();

        var (vm2, manager2, _) = Create();
        manager2.LastCheckUtc = DateTimeOffset.UtcNow;

        vm2.EnsureFirstCheck();

        manager2.CheckCallCount.Should().Be(0);
    }
}
