using System.Diagnostics;

namespace AiMeter.Services.Updates;

/// <summary>Default launcher backed by <see cref="Process.Start(ProcessStartInfo)"/>.</summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    /// <inheritdoc />
    public Process Start(ProcessStartInfo startInfo)
    {
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The process could not be started.");
    }
}
