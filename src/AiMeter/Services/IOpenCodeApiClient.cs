using System.Threading.Tasks;

namespace AiMeter.Services;

public interface IOpenCodeApiClient
{
    /// <summary>
    /// Fetches an opencode.ai API path and returns (HTTP status, response body). The optional
    /// <paramref name="orgId"/> is sent as the console's <c>x-org-id</c> header.
    /// </summary>
    Task<(int StatusCode, string? Body)> GetAsync(string path, string? orgId = null);
    string CurrentUrl { get; }
}
