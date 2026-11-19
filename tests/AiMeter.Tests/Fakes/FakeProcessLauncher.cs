using System.Diagnostics;
using AiMeter.Services.Updates;

namespace AiMeter.Tests.Fakes;

/// <summary>Records the processes view models ask to launch instead of starting them.</summary>
public sealed class FakeProcessLauncher : IProcessLauncher
{
    public List<ProcessStartInfo> StartInfos { get; } = new();

    public Process Start(ProcessStartInfo startInfo)
    {
        StartInfos.Add(startInfo);
        return new Process();
    }
}
