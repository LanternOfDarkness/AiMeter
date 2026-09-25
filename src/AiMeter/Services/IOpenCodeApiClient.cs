using System.Collections.Generic;
using System.Threading.Tasks;

namespace AiMeter.Services;

/// <summary>One API response the page made while loading. OrgId is the request's x-org-id header.</summary>
public record CapturedResponse(string Path, int StatusCode, string? Body, string? OrgId);

/// <summary>Everything captured during one page load. BouncedToLogin = the SPA redirected to its login screen.</summary>
public record CaptureResult(IReadOnlyList<CapturedResponse> Responses, bool BouncedToLogin, string FinalUrl);

public interface IOpenCodeApiClient
{
    /// <summary>Fetches an opencode.ai API path and returns (HTTP status, response body).</summary>
    Task<(int StatusCode, string? Body)> GetAsync(string path);
    string CurrentUrl { get; }

    /// <summary>
    /// Loads <paramref name="pageUrl"/> in the logged-in browser and captures the page's own GET
    /// responses for API paths ending in any of <paramref name="apiPaths"/>, so the site applies
    /// its own auth (bearer token, org header). Waits until <paramref name="apiPaths"/>[0] has
    /// answered (plus a short settle for the rest), the page bounces to login, or a timeout.
    /// </summary>
    Task<CaptureResult> CaptureResponsesAsync(string pageUrl, IReadOnlyList<string> apiPaths);

    /// <summary>
    /// Navigates the logged-in browser to <paramref name="url"/>, following any auth
    /// redirects, and returns where it settled plus that page's HTML (null on timeout or if it
    /// settled off opencode.ai, e.g. on the auth.opencode.ai login screen).
    /// </summary>
    Task<(string FinalUrl, string? Html)> NavigateAndReadAsync(string url);

    /// <summary>Reads a localStorage item on the opencode.ai origin (null if absent).</summary>
    Task<string?> GetLocalStorageAsync(string key);

    /// <summary>Writes (or, with null, removes) a localStorage item on the opencode.ai origin.</summary>
    Task SetLocalStorageAsync(string key, string? value);
}
