namespace AiMeter.Services.Updates;

/// <summary>
/// Marshals callbacks onto the UI thread without making view models depend on WPF directly.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>True when the calling thread owns the UI dispatcher.</summary>
    bool IsOnDispatcherThread { get; }

    /// <summary>Runs <paramref name="action"/> on the UI thread, or synchronously when none exists.</summary>
    void Post(Action action);
}
