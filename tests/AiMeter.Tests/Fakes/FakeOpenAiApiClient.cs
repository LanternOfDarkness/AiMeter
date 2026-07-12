using System.Collections.Generic;
using System.Threading.Tasks;
using AiMeter.Services;

namespace AiMeter.Tests.Fakes;

/// <summary>
/// Returns canned (status, body) responses keyed by request path, and records the Bearer token
/// (API key) each call was made with so tests can assert the key is forwarded.
/// </summary>
public sealed class FakeOpenAiApiClient : IOpenAiApiClient
{
    private readonly Dictionary<string, (int, string?)> _responses = new();

    public List<string> RequestedPaths { get; } = new();
    public List<string?> BearerTokens { get; } = new();

    public FakeOpenAiApiClient When(string path, int status, string? body)
    {
        _responses[path] = (status, body);
        return this;
    }

    public Task<(int StatusCode, string? Body)> GetAsync(string path, string? bearerToken = null)
    {
        RequestedPaths.Add(path);
        BearerTokens.Add(bearerToken);
        return Task.FromResult(_responses.TryGetValue(path, out var r) ? r : (404, (string?)null));
    }
}
