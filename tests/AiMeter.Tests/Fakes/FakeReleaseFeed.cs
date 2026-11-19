using System;
using System.Threading;
using System.Threading.Tasks;
using AiMeter.Services.Updates;

namespace AiMeter.Tests.Fakes;

/// <summary>Scripted <see cref="IReleaseFeed"/> that records call count.</summary>
public sealed class FakeReleaseFeed : IReleaseFeed
{
    private readonly object _lock = new();
    private TaskCompletionSource<bool> _called = NewSignal();

    public ReleaseInfo? Result { get; set; }

    public Exception? Exception { get; set; }

    public int CallCount { get; private set; }

    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        TaskCompletionSource<bool> called;
        lock (_lock)
        {
            CallCount++;
            called = _called;
            _called = NewSignal();
        }

        called.TrySetResult(true);

        if (Exception is not null) throw Exception;

        return Task.FromResult(Result);
    }

    /// <summary>Completes when the feed is called again; times out so a regression fails instead of hanging.</summary>
    public async Task WaitForCallAsync()
    {
        Task signal;
        lock (_lock)
        {
            signal = _called.Task;
        }

        await signal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
