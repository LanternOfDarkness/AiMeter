using System.Collections.Generic;
using System.Threading.Tasks;
using AiMeter.Services;

namespace AiMeter.Tests.Fakes;

public sealed class FakeClaudeApiClient : IClaudeApiClient
{
    private readonly Dictionary<string, (int Status, string? Body)> _responses = new();
    public List<string> RequestedPaths { get; } = new();

    public FakeClaudeApiClient When(string path, int status, string? body)
    {
        _responses[path] = (status, body);
        return this;
    }

    public Task<(int StatusCode, string? Body)> GetAsync(string path)
    {
        RequestedPaths.Add(path);
        if (_responses.TryGetValue(path, out var res))
        {
            return Task.FromResult(res);
        }
        return Task.FromResult((404, (string?)null));
    }
}
