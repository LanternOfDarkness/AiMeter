using AiMeter.Services;

namespace AiMeter.Tests.Fakes;

/// <summary>In-memory IStartupManager stub (no registry access in tests).</summary>
public sealed class FakeStartupManager : IStartupManager
{
    public bool IsEnabled { get; private set; }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}
