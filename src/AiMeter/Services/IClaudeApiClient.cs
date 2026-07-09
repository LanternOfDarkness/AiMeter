using System.Threading.Tasks;

namespace AiMeter.Services;

public interface IClaudeApiClient
{
    /// <summary>Fetches a claude.ai API path and returns (HTTP status, response body).</summary>
    Task<(int StatusCode, string? Body)> GetAsync(string path);
}
