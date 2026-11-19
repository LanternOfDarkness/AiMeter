using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiMeter.Services.Updates;

namespace AiMeter.Tests.Fakes;

/// <summary>Records install calls and returns a configurable preflight/result.</summary>
public sealed class FakeUpdateInstaller : IUpdateInstaller
{
    public UpdatePreflight Preflight { get; set; } = new(true, null);

    public InstallResult Result { get; set; } = new(true, null);

    public int CheckCallCount { get; private set; }

    public List<string> AppliedPaths { get; } = new();

    public UpdatePreflight CheckCanUpdateInPlace()
    {
        CheckCallCount++;
        return Preflight;
    }

    public Task<InstallResult> ApplyAndRestartAsync(string stagedExePath, CancellationToken ct)
    {
        AppliedPaths.Add(stagedExePath);
        return Task.FromResult(Result);
    }
}
