using AiMeter.Services.Updates;

namespace AiMeter.Tests.Fakes;

/// <summary>Runs posted callbacks inline, so view model refreshes are observable immediately.</summary>
public sealed class FakeUiDispatcher : IUiDispatcher
{
    public bool IsOnDispatcherThread => true;

    public int PostCount { get; private set; }

    public void Post(Action action)
    {
        PostCount++;
        action();
    }
}
