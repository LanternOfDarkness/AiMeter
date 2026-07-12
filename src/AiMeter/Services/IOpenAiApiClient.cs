using System.Threading.Tasks;

namespace AiMeter.Services;

public interface IOpenAiApiClient
{
    /// <summary>
    /// Fetches an api.openai.com path and returns (HTTP status, response body). When
    /// <paramref name="bearerToken"/> is supplied it is sent as <c>Authorization: Bearer</c> —
    /// the billing/usage endpoints require the user's API key. A status of 0 indicates a
    /// transport-level failure (no HTTP response).
    /// </summary>
    Task<(int StatusCode, string? Body)> GetAsync(string path, string? bearerToken = null);
}
