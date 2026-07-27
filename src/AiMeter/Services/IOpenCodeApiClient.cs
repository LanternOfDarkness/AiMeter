using System.Threading.Tasks;

namespace AiMeter.Services;

public interface IOpenCodeApiClient
{
    /// <summary>Fetches an opencode.ai API path and returns (HTTP status, response body).</summary>
    Task<(int StatusCode, string? Body)> GetAsync(string path);
    string CurrentUrl { get; }
}
