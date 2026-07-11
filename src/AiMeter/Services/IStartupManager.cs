namespace AiMeter.Services;

/// <summary>Toggles whether AiMeter launches automatically when the user signs into Windows.</summary>
public interface IStartupManager
{
    /// <summary>True if AiMeter is currently registered to launch at sign-in.</summary>
    bool IsEnabled { get; }

    /// <summary>Registers (or removes) AiMeter from Windows startup.</summary>
    void SetEnabled(bool enabled);
}
