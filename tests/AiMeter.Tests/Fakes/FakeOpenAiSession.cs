using AiMeter.Services;

namespace AiMeter.Tests.Fakes;

/// <summary>In-memory IOpenAiSession stub — holds the API key without touching DPAPI/settings.</summary>
public sealed class FakeOpenAiSession : IOpenAiSession
{
    public string ProviderName => "OpenAI";
    public string? ApiKey { get; set; }

    public bool HasSession => !string.IsNullOrEmpty(ApiKey);

    public void Store(string apiKey) => ApiKey = apiKey;
    public string? GetApiKey() => ApiKey;
    public void Clear() => ApiKey = null;
}
