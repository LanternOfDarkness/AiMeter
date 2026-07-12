namespace AiMeter.Services;

/// <summary>
/// Login state for the OpenAI Platform provider. Unlike the browser-session providers this is
/// backed by an API key the user pastes in Settings (there is no OAuth round trip), so it adds
/// key storage/retrieval on top of the common <see cref="IProviderSession"/> surface.
/// </summary>
public interface IOpenAiSession : IProviderSession
{
    /// <summary>Encrypts and persists the API key (empty/whitespace clears it instead).</summary>
    void Store(string apiKey);

    /// <summary>Returns the decrypted API key, or null if none is stored / decryption fails.</summary>
    string? GetApiKey();
}
