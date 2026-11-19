using System.Diagnostics;

namespace AiMeter.Services.Updates;

/// <summary>Starts processes; injectable so update tests never launch a real executable.</summary>
public interface IProcessLauncher
{
    /// <summary>Starts a process described by <paramref name="startInfo"/>.</summary>
    Process Start(ProcessStartInfo startInfo);
}
