namespace AiMeter.Services.Updates;

/// <summary>Persists <see cref="UpdateState"/> across restarts.</summary>
public interface IUpdateStateStore
{
    /// <summary>Reads the stored state; returns an empty state when absent or unreadable.</summary>
    UpdateState Load();

    /// <summary>Writes the state atomically.</summary>
    void Save(UpdateState state);
}
