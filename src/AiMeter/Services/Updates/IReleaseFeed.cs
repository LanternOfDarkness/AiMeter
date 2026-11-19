using System.Threading;
using System.Threading.Tasks;

namespace AiMeter.Services.Updates;

/// <summary>Source of the latest published release.</summary>
public interface IReleaseFeed
{
    /// <summary>
    /// Fetches the latest stable release, or <see langword="null"/> when there is no usable
    /// release (no releases, draft/prerelease, or a payload that fails validation).
    /// </summary>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct);
}
