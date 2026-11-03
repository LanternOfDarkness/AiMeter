using System.Threading.Tasks;

namespace AiMeter.Services;

/// <summary>
/// A console page as rendered by the logged-in browser. Html is the live DOM (null if the page
/// never loaded); Ready = the ready check passed before the timeout; BouncedToLogin = the site
/// sent us to its login screen instead.
/// </summary>
public record RenderedPage(string FinalUrl, string? Html, bool Ready, bool BouncedToLogin);

public interface IOpenCodeApiClient
{
    /// <summary>Fetches an opencode.ai API path and returns (HTTP status, response body).</summary>
    Task<(int StatusCode, string? Body)> GetAsync(string path);
    string CurrentUrl { get; }

    /// <summary>
    /// Loads <paramref name="url"/> in the logged-in browser and, since the console is a SPA that
    /// renders after the document loads, polls the JavaScript expression
    /// <paramref name="readyCheck"/> until it is truthy, the page bounces to login, or a timeout.
    /// Returns the DOM at that point.
    /// </summary>
    Task<RenderedPage> ReadRenderedPageAsync(string url, string readyCheck);
}
