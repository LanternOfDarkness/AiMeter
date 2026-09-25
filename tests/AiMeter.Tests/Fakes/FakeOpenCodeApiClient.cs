using System;
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

    /// <summary>In-memory stand-in for the browser's localStorage.</summary>
    public Dictionary<string, string> LocalStorage { get; } = new();

    /// <summary>Builds each page load's capture from the current localStorage (so org switches can be simulated).</summary>
    public Func<FakeOpenCodeApiClient, CaptureResult> OnCapture { get; set; } =
        _ => new CaptureResult(Array.Empty<CapturedResponse>(), false, string.Empty);

    public int CaptureCount { get; private set; }

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

    public Task<CaptureResult> CaptureResponsesAsync(string pageUrl, IReadOnlyList<string> apiPaths)
    {
        CaptureCount++;
        return Task.FromResult(OnCapture(this));
    }

    /// <summary>Canned page HTML for NavigateAndReadAsync, keyed by URL (missing = stuck on login).</summary>
    public Dictionary<string, string> Pages { get; } = new();

    public List<string> NavigatedUrls { get; } = new();

    public Task<(string FinalUrl, string? Html)> NavigateAndReadAsync(string url)
    {
        NavigatedUrls.Add(url);
        return Task.FromResult(Pages.TryGetValue(url, out var html)
            ? (url, (string?)html)
            : ("https://auth.opencode.ai/authorize", (string?)null));
    }

    public Task<string?> GetLocalStorageAsync(string key) =>
        Task.FromResult(LocalStorage.TryGetValue(key, out var v) ? v : null);

    public Task SetLocalStorageAsync(string key, string? value)
    {
        if (value is null) LocalStorage.Remove(key);
        else LocalStorage[key] = value;
        return Task.CompletedTask;
    }
}
