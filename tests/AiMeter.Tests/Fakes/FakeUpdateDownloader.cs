using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiMeter.Services.Updates;

namespace AiMeter.Tests.Fakes;

/// <summary>In-memory <see cref="IUpdateDownloader"/> that records calls and never touches disk.</summary>
public sealed class FakeUpdateDownloader : IUpdateDownloader
{
    private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int DownloadCallCount { get; private set; }

    public int CleanupCallCount { get; private set; }

    public string? LastDestinationDirectory { get; private set; }

    public List<DownloadProgress> ProgressReports { get; } = new();

    public string StagedPath { get; set; } = @"C:\temp\AiMeter-staged.exe";

    public Exception? Exception { get; set; }

    /// <summary>When set, <see cref="DownloadAsync"/> waits for it before completing.</summary>
    public TaskCompletionSource<bool>? Gate { get; set; }

    /// <summary>Progress reported in addition to <see cref="ProgressReports"/>.</summary>
    public DownloadProgress? ProgressToReport { get; set; }

    public Task WaitForStartAsync() => _started.Task.WaitAsync(TimeSpan.FromSeconds(5));

    public Task<string> DownloadAsync(
        ReleaseInfo release,
        string destinationDirectory,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        DownloadCallCount++;
        LastDestinationDirectory = destinationDirectory;
        _started.TrySetResult(true);

        if (ProgressToReport is { } reported)
        {
            ProgressReports.Add(reported);
            progress?.Report(reported);
        }

        return DownloadCoreAsync(progress, ct);
    }

    public int CleanupStagedFiles()
    {
        CleanupCallCount++;
        return 0;
    }

    private async Task<string> DownloadCoreAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (Gate is not null) await Gate.Task.WaitAsync(ct);

        if (Exception is not null) throw Exception;

        return StagedPath;
    }
}
