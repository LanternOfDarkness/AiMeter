using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiMeter.Services;

namespace AiMeter.Tests.Fakes;

/// <summary>Returns canned (status, body) responses keyed by request path, and canned rendered pages keyed by URL.</summary>
public sealed class FakeOpenCodeApiClient : IOpenCodeApiClient
{
    private readonly Dictionary<string, (int, string?)> _responses = new();

    public List<string> RequestedPaths { get; } = new();

    public string CurrentUrl { get; set; } = string.Empty;

    /// <summary>Rendered pages keyed by requested URL (missing = timed out with nothing rendered).</summary>
    public Dictionary<string, RenderedPage> Pages { get; } = new();

    public List<string> NavigatedUrls { get; } = new();

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

    public Task<RenderedPage> ReadRenderedPageAsync(string url, string readyCheck)
    {
        NavigatedUrls.Add(url);
        return Task.FromResult(Pages.TryGetValue(url, out var page) ? page : new RenderedPage(url, "<html></html>", false, false));
    }
}
