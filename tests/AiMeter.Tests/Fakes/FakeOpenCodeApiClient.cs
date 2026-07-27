using System.Collections.Generic;
using System.Threading.Tasks;
using AiMeter.Services;

namespace AiMeter.Tests.Fakes;

/// <summary>Returns canned (status, body) responses keyed by request path.</summary>
public sealed class FakeOpenCodeApiClient : IOpenCodeApiClient
{
    private readonly Dictionary<string, (int, string?)> _responses = new();

    public List<string> RequestedPaths { get; } = new();

    public string CurrentUrl { get; set; } = string.Empty;

    public FakeOpenCodeApiClient When(string path, int status, string? body)
    {
        _responses[path] = (status, body);
        return this;
    }

    public Task<(int StatusCode, string? Body)> GetAsync(string path)
    {
        RequestedPaths.Add(path);
        return Task.FromResult(_responses.TryGetValue(path, out var r) ? r : (404, (string?)null));
    }
}
