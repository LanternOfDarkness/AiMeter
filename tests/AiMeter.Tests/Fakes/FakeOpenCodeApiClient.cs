using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiMeter.Services;

namespace AiMeter.Tests.Fakes;

/// <summary>Returns canned (status, body) responses keyed by API path and records every request.</summary>
public sealed class FakeOpenCodeApiClient : IOpenCodeApiClient
{
    private readonly Dictionary<string, (int, string?)> _responses = new();

    public List<(string Path, string? OrgId)> Requests { get; } = new();

    public List<string> RequestedPaths => Requests.Select(r => r.Path).ToList();

    public string CurrentUrl { get; set; } = string.Empty;

    public FakeOpenCodeApiClient When(string path, int status, string? body)
    {
        _responses[path] = (status, body);
        return this;
    }

    public Task<(int StatusCode, string? Body)> GetAsync(string path, string? orgId = null)
    {
        Requests.Add((path, orgId));
        return Task.FromResult(_responses.TryGetValue(path, out var r) ? r : (404, (string?)null));
    }
}
