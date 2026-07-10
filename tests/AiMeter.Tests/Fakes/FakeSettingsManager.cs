using AiMeter.Managers;
using AiMeter.Models;

namespace AiMeter.Tests.Fakes;

/// <summary>
/// Disk-free ISettingsManager for tests. Holds a live AppConfig and counts Save calls
/// so tests can assert whether persistence (or anything that triggers it) ran.
/// </summary>
public sealed class FakeSettingsManager : ISettingsManager
{
    public AppConfig Current { get; set; } = new();

    public int SaveCallCount { get; private set; }

    public void Load() { }

    public void Save() => SaveCallCount++;
}