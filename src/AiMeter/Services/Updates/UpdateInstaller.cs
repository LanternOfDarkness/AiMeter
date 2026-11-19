using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace AiMeter.Services.Updates;

/// <summary>Outcome of an in-place replacement attempt.</summary>
public sealed record InstallResult(bool RestartStarted, string? Error);

/// <summary>Whether this instance may replace its own executable in place.</summary>
public sealed record UpdatePreflight(bool CanUpdateInPlace, string? Reason);

/// <summary>Replaces the running AiMeter.exe with a staged build and relaunches it.</summary>
public interface IUpdateInstaller
{
    /// <summary>Re-probes the environment on every call; nothing is cached.</summary>
    UpdatePreflight CheckCanUpdateInPlace();

    /// <summary>
    /// Moves the staged executable over the running one, keeping the previous file as a backup,
    /// and starts the new instance. Failures restore the backup and are returned as an error.
    /// </summary>
    Task<InstallResult> ApplyAndRestartAsync(string stagedExePath, CancellationToken ct);
}

/// <summary>
/// Installs an update by renaming the running executable aside (Windows allows renaming a
/// loaded image), placing the new file at the original path, and starting it with
/// <c>--restarted-from &lt;pid&gt;</c>. The previous file is kept as <c>AiMeter.exe.old</c>
/// until the new instance has started.
/// </summary>
public sealed class UpdateInstaller : IUpdateInstaller
{
    private const string ExecutableName = "AiMeter.exe";
    private const string WriteProbeFileName = ".aimeter-write-test";
    private const int AppModelErrorNoPackage = 15700;
    private const int WriteProbeAttempts = 3;
    private const int WriteProbeRetryDelayMilliseconds = 50;

    private readonly IProcessLauncher _launcher;
    private readonly ILogger<UpdateInstaller>? _logger;
    private readonly Func<int> _packageFullNameProbe;
    private readonly Func<string?> _processPathProvider;
    private readonly Func<bool> _developmentBuildProbe;

    /// <summary>
    /// Creates an installer. Optional parameters exist so tests can replace the environment
    /// probes; Microsoft DI resolves the defaults.
    /// </summary>
    public UpdateInstaller(
        IProcessLauncher launcher,
        ILogger<UpdateInstaller>? logger = null,
        Func<int>? packageFullNameProbe = null,
        Func<string?>? processPathProvider = null,
        Func<bool>? developmentBuildProbe = null)
    {
        _launcher = launcher;
        _logger = logger;
        _packageFullNameProbe = packageFullNameProbe ?? GetCurrentPackageFullNameStatus;
        _processPathProvider = processPathProvider ?? (() => Environment.ProcessPath);
        _developmentBuildProbe = developmentBuildProbe ?? IsDevelopmentBuild;
    }

    /// <inheritdoc />
    public UpdatePreflight CheckCanUpdateInPlace()
    {
        if (_developmentBuildProbe())
        {
            return new UpdatePreflight(false, "This is a development build; automatic updates are disabled.");
        }

        int packageStatus;
        try
        {
            packageStatus = _packageFullNameProbe();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Package identity probe failed; assuming a packaged install.");
            return new UpdatePreflight(false, "Updates are handled by the Microsoft Store.");
        }

        if (packageStatus != AppModelErrorNoPackage)
        {
            _logger?.LogInformation("Package identity status {Status}; updates are handled by the Store.", packageStatus);
            return new UpdatePreflight(false, "Updates are handled by the Microsoft Store.");
        }

        var processPath = _processPathProvider();
        if (string.IsNullOrEmpty(processPath)
            || !string.Equals(Path.GetFileName(processPath), ExecutableName, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(processPath))
        {
            _logger?.LogDebug("Process path {ProcessPath} is not the installed AiMeter.exe.", processPath);
            return new UpdatePreflight(false, "AiMeter is not running from its installed AiMeter.exe.");
        }

        var directory = Path.GetDirectoryName(processPath);
        if (string.IsNullOrEmpty(directory) || !CanWriteToAppDirectory(directory))
        {
            return new UpdatePreflight(false, "The AiMeter folder is not writable; install the update from the release page.");
        }

        return new UpdatePreflight(true, null);
    }

    /// <inheritdoc />
    public Task<InstallResult> ApplyAndRestartAsync(string stagedExePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyAndRestart(stagedExePath));
    }

    private InstallResult ApplyAndRestart(string stagedExePath)
    {
        var target = _processPathProvider();
        var appDirectory = string.IsNullOrEmpty(target) ? null : Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(appDirectory) || !File.Exists(target))
        {
            _logger?.LogWarning("Cannot locate the running AiMeter.exe (process path {ProcessPath}).", target);
            return new InstallResult(false, "Cannot locate the running AiMeter.exe.");
        }

        string backupPath;
        try
        {
            backupPath = MoveTargetAside(target);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Could not move {Target} aside; nothing was replaced.", target);
            return new InstallResult(false, $"Could not replace the running AiMeter.exe: {ex.Message}");
        }

        _logger?.LogInformation("Replacing {Target} with {Staged}; previous file kept at {Backup}.", target, stagedExePath, backupPath);

        try
        {
            PlaceNewExecutable(stagedExePath, target);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Could not place the new AiMeter.exe at {Target}.", target);
            var restoreError = RestoreBackup(target, backupPath);
            return new InstallResult(false, restoreError ?? $"Could not install the update: {ex.Message}");
        }

        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = false,
            WorkingDirectory = appDirectory,
        };
        startInfo.ArgumentList.Add("--restarted-from");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        try
        {
            using var process = _launcher.Start(startInfo);
            _logger?.LogInformation("Started the updated AiMeter.exe from {Target}.", target);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Could not start the updated AiMeter.exe from {Target}.", target);
            var restoreError = RestoreBackup(target, backupPath);
            return new InstallResult(false, restoreError ?? $"Could not start the updated AiMeter.exe: {ex.Message}");
        }

        return new InstallResult(true, null);
    }

    private string MoveTargetAside(string target)
    {
        var preferredBackup = target + ".old";
        var backupPath = preferredBackup;
        if (File.Exists(preferredBackup))
        {
            try
            {
                File.Delete(preferredBackup);
            }
            catch (Exception ex)
            {
                backupPath = target + ".old-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                _logger?.LogDebug(ex, "Could not delete {PreferredBackup}; using {Backup} instead.", preferredBackup, backupPath);
            }
        }

        File.Move(target, backupPath);
        return backupPath;
    }

    private void PlaceNewExecutable(string stagedExePath, string target)
    {
        if (IsSameVolume(stagedExePath, target))
        {
            File.Move(stagedExePath, target);
            return;
        }

        File.Copy(stagedExePath, target, overwrite: false);
        try
        {
            File.Delete(stagedExePath);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not delete the staged file {Staged}.", stagedExePath);
        }
    }

    private string? RestoreBackup(string target, string backupPath)
    {
        TryDelete(target);
        try
        {
            File.Move(backupPath, target);
            _logger?.LogWarning("Restored the previous AiMeter.exe from {Backup}.", backupPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Could not restore the previous AiMeter.exe from {Backup}.", backupPath);
            return $"The update failed and the previous AiMeter.exe could not be restored. It is kept at {backupPath}.";
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not delete {Path} before restoring the backup.", path);
        }
    }

    private static bool IsSameVolume(string firstPath, string secondPath)
    {
        var firstRoot = Path.GetPathRoot(firstPath);
        var secondRoot = Path.GetPathRoot(secondPath);
        return !string.IsNullOrEmpty(firstRoot)
            && !string.IsNullOrEmpty(secondRoot)
            && string.Equals(firstRoot, secondRoot, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanWriteToAppDirectory(string directory)
    {
        var probePath = Path.Combine(directory, WriteProbeFileName);
        for (var attempt = 1; attempt <= WriteProbeAttempts; attempt++)
        {
            try
            {
                File.WriteAllText(probePath, string.Empty);
                File.Delete(probePath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    File.Delete(probePath);
                }
                catch
                {
                    // The probe file may not exist yet; nothing to clean up.
                }

                if (attempt == WriteProbeAttempts)
                {
                    _logger?.LogDebug(ex, "Write probe failed for {Directory}.", directory);
                    return false;
                }

                Thread.Sleep(WriteProbeRetryDelayMilliseconds);
            }
        }

        return false;
    }

    private static bool IsDevelopmentBuild()
    {
#if DEBUG
        return true;
#else
        if (Debugger.IsAttached)
        {
            return true;
        }

        // A non-empty entry-assembly location means a framework-dependent bin build, not the
        // published single-file executable.
        return !string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location);
#endif
    }

    private static int GetCurrentPackageFullNameStatus()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, 0);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);
}
