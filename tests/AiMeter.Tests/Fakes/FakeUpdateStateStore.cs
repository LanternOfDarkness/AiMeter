using AiMeter.Services.Updates;

namespace AiMeter.Tests.Fakes;

/// <summary>In-memory <see cref="IUpdateStateStore"/> that records saves.</summary>
public sealed class FakeUpdateStateStore : IUpdateStateStore
{
    public UpdateState State { get; set; } = new();

    public int SaveCallCount { get; private set; }

    public UpdateState Load() => State;

    public void Save(UpdateState state)
    {
        State = state;
        SaveCallCount++;
    }
}
