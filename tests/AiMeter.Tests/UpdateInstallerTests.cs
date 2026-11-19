using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AiMeter.Services.Updates;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiMeter.Tests;

public class UpdateInstallerTests
{
    private const int AppModelErrorNoPackage = 15700;

    private static string NewTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AiMeterUpdateTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static UpdateInstaller CreateInstaller(
        IProcessLauncher launcher,
        string? processPath,
        Func<bool>? developmentBuildProbe = null,
        Func<int>? packageFullNameProbe = null)
    {
        return new UpdateInstaller(
            launcher,
            NullLogger<UpdateInstaller>.Instance,
            packageFullNameProbe ?? (() => AppModelErrorNoPackage),
            () => processPath,
            developmentBuildProbe ?? (() => false));
    }

    [Fact]
    public async Task Apply_keeps_the_previous_exe_as_backup_and_places_the_staged_file()
    {
        var directory = NewTempDirectory();
        try
        {
            var target = Path.Combine(directory, "AiMeter.exe");
            var staged = Path.Combine(directory, "staged.exe");
            File.WriteAllText(target, "old build");
            File.WriteAllText(staged, "new build");
            var launcher = new FakeProcessLauncher();

            var result = await CreateInstaller(launcher, target).ApplyAndRestartAsync(staged, CancellationToken.None);

            result.RestartStarted.Should().BeTrue();
            result.Error.Should().BeNull();
            File.ReadAllText(target).Should().Be("new build");
            File.ReadAllText(target + ".old").Should().Be("old build");
            File.Exists(staged).Should().BeFalse("the staged file is moved into place");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Apply_restores_the_backup_when_the_staged_file_cannot_be_moved()
    {
        var directory = NewTempDirectory();
        try
        {
            var target = Path.Combine(directory, "AiMeter.exe");
            var staged = Path.Combine(directory, "staged.exe");
            File.WriteAllText(target, "old build");
            File.WriteAllText(staged, "new build");
            var launcher = new FakeProcessLauncher();

            InstallResult result;
            using (new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                result = await CreateInstaller(launcher, target).ApplyAndRestartAsync(staged, CancellationToken.None);
            }

            result.RestartStarted.Should().BeFalse();
            result.Error.Should().NotBeNullOrWhiteSpace();
            File.ReadAllText(target).Should().Be("old build");
            File.Exists(target + ".old").Should().BeFalse("the backup was moved back into place");
            launcher.Started.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Apply_restores_the_backup_when_the_launcher_throws()
    {
        var directory = NewTempDirectory();
        try
        {
            var target = Path.Combine(directory, "AiMeter.exe");
            var staged = Path.Combine(directory, "staged.exe");
            File.WriteAllText(target, "old build");
            File.WriteAllText(staged, "new build");
            var launcher = new FakeProcessLauncher { ThrowOnStart = new InvalidOperationException("launch failed") };

            var result = await CreateInstaller(launcher, target).ApplyAndRestartAsync(staged, CancellationToken.None);

            result.RestartStarted.Should().BeFalse();
            result.Error.Should().Contain("launch failed");
            File.ReadAllText(target).Should().Be("old build");
            File.Exists(target + ".old").Should().BeFalse("the backup was moved back into place");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Apply_starts_the_replacement_exactly_once_and_passes_the_restart_argument()
    {
        var directory = NewTempDirectory();
        try
        {
            var target = Path.Combine(directory, "AiMeter.exe");
            var staged = Path.Combine(directory, "staged.exe");
            File.WriteAllText(target, "old build");
            File.WriteAllText(staged, "new build");
            var launcher = new FakeProcessLauncher();

            var result = await CreateInstaller(launcher, target).ApplyAndRestartAsync(staged, CancellationToken.None);

            result.RestartStarted.Should().BeTrue();
            launcher.Started.Should().ContainSingle();
            var startInfo = launcher.Started[0];
            startInfo.FileName.Should().Be(target);
            startInfo.WorkingDirectory.Should().Be(directory);
            startInfo.UseShellExecute.Should().BeFalse();
            startInfo.ArgumentList.Should().Contain("--restarted-from");
            startInfo.ArgumentList.Should().Contain(Environment.ProcessId.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Apply_reports_an_error_when_the_running_exe_cannot_be_located()
    {
        var directory = NewTempDirectory();
        try
        {
            var launcher = new FakeProcessLauncher();
            var missing = Path.Combine(directory, "AiMeter.exe");

            var result = await CreateInstaller(launcher, missing)
                .ApplyAndRestartAsync(Path.Combine(directory, "staged.exe"), CancellationToken.None);

            result.RestartStarted.Should().BeFalse();
            result.Error.Should().Be("Cannot locate the running AiMeter.exe.");
            launcher.Started.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CheckIs_false_for_a_development_build()
    {
        var directory = NewTempDirectory();
        try
        {
            var target = Path.Combine(directory, "AiMeter.exe");
            File.WriteAllText(target, "exe");

            var preflight = CreateInstaller(new FakeProcessLauncher(), target, developmentBuildProbe: () => true)
                .CheckCanUpdateInPlace();

            preflight.CanUpdateInPlace.Should().BeFalse();
            preflight.Reason.Should().Contain("development build");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CheckIs_false_when_the_app_runs_from_a_package()
    {
        var directory = NewTempDirectory();
        try
        {
            var target = Path.Combine(directory, "AiMeter.exe");
            File.WriteAllText(target, "exe");

            var preflight = CreateInstaller(new FakeProcessLauncher(), target, packageFullNameProbe: () => 0)
                .CheckCanUpdateInPlace();

            preflight.CanUpdateInPlace.Should().BeFalse();
            preflight.Reason.Should().Be("Updates are handled by the Microsoft Store.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CheckIs_false_when_the_app_folder_is_not_writable()
    {
        var directory = NewTempDirectory();
        try
        {
            var target = Path.Combine(directory, "AiMeter.exe");
            File.WriteAllText(target, "exe");
            Directory.CreateDirectory(Path.Combine(directory, ".aimeter-write-test"));

            var preflight = CreateInstaller(new FakeProcessLauncher(), target).CheckCanUpdateInPlace();

            preflight.CanUpdateInPlace.Should().BeFalse();
            preflight.Reason.Should().Contain("not writable");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CheckIs_true_for_a_single_file_build_in_a_writable_folder()
    {
        var directory = NewTempDirectory();
        try
        {
            var target = Path.Combine(directory, "AiMeter.exe");
            File.WriteAllText(target, "exe");

            var preflight = CreateInstaller(new FakeProcessLauncher(), target).CheckCanUpdateInPlace();

            preflight.CanUpdateInPlace.Should().BeTrue();
            preflight.Reason.Should().BeNull();
            File.Exists(Path.Combine(directory, ".aimeter-write-test")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        public List<ProcessStartInfo> Started { get; } = new();

        public Exception? ThrowOnStart { get; set; }

        public Process Start(ProcessStartInfo startInfo)
        {
            if (ThrowOnStart != null)
            {
                throw ThrowOnStart;
            }

            Started.Add(startInfo);
            return new Process();
        }
    }
}
