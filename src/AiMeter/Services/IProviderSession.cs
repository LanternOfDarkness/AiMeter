namespace AiMeter.Services;

/// <summary>
/// The login state of a single usage provider (Claude, OpenCode, …). Lets the Settings
/// ACCOUNTS section render one generic row per provider instead of hardcoding each one.
/// The provider-specific <c>Store(...)</c> stays on the concrete session interfaces since
/// each captures a different cookie.
/// </summary>
public interface IProviderSession
{
    /// <summary>Display name for the account row and its login button, e.g. "Claude".</summary>
    string ProviderName { get; }

    /// <summary>True once the user has logged in and the session marker is stored.</summary>
    bool HasSession { get; }

    /// <summary>Forgets the stored session marker (logout / invalidation on 401/403).</summary>
    void Clear();

    /// <summary>
    /// Latest info from the provider about this logged-in account that the widget can't show
    /// as a bar (e.g. "No active Go subscription"); null while usage loads normally.
    /// </summary>
    string? StatusNote => null;

    /// <summary>Raised when <see cref="StatusNote"/> changes.</summary>
    event System.EventHandler? StatusNoteChanged { add { } remove { } }
}
