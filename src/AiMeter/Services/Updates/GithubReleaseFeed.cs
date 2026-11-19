using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AiMeter.Services.Updates;

/// <summary>
/// Reads the latest stable release of <c>LanternOfDarkness/AiMeter</c> from the GitHub
/// Releases API and maps it onto <see cref="ReleaseInfo"/>. Malformed or untrustworthy
/// payloads are reported as <see langword="null"/> rather than surfaced as an update.
/// </summary>
public sealed class GithubReleaseFeed : IReleaseFeed, IDisposable
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/LanternOfDarkness/AiMeter/releases/latest";
    private const string ApiVersion = "2022-11-28";
    private const string AssetName = "AiMeter.exe";
    private const string ChecksumAssetName = "AiMeter.exe.sha256";
    private const long MaxAssetSize = 200L * 1024 * 1024;
    private const long MaxChecksumAssetSize = 4096;
    private const int MaxNotesLength = 8000;
    private const double TimeoutSeconds = 15;

    private readonly HttpClient _client;
    private readonly ILogger<GithubReleaseFeed>? _logger;

    /// <summary>Creates the feed used by the application container.</summary>
    public GithubReleaseFeed(ILogger<GithubReleaseFeed>? logger = null)
        : this(new HttpClientHandler(), logger)
    {
    }

    /// <summary>Creates the feed with a caller-supplied handler, for tests.</summary>
    public GithubReleaseFeed(HttpMessageHandler handler, ILogger<GithubReleaseFeed>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _client = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        using var request = CreateRequest();
        HttpResponseMessage response;

        try
        {
            response = await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new ReleaseFeedException("The GitHub release request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ReleaseFeedException("The GitHub release request failed.", ex);
        }

        using (response)
        {
            return await ReadResponseAsync(response, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Releases the underlying <see cref="HttpClient"/>.</summary>
    public void Dispose() => _client.Dispose();

    private HttpRequestMessage CreateRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ResolveRequestUri());
        request.Headers.UserAgent.ParseAdd("AiMeter/" + AppInfo.Current);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);
        return request;
    }

    private static Uri ResolveRequestUri()
    {
#if DEBUG
        var overrideUrl = Environment.GetEnvironmentVariable("AIMETER_UPDATE_FEED");
        if (!string.IsNullOrWhiteSpace(overrideUrl)
            && Uri.TryCreate(overrideUrl.Trim(), UriKind.Absolute, out var customUri))
        {
            return customUri;
        }
#endif

        return new Uri(LatestReleaseUrl, UriKind.Absolute);
    }

    private async Task<ReleaseInfo?> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = response.StatusCode;

        if (status == HttpStatusCode.NotFound)
        {
            _logger?.LogInformation("GitHub reported no latest release (HTTP 404)");
            return null;
        }

        if (status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests && IsRateLimited(response))
        {
            _logger?.LogWarning("GitHub release request was rate limited (HTTP {StatusCode})", (int)status);
            throw new ReleaseFeedRateLimitedException();
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogWarning("GitHub release request failed with HTTP {StatusCode}", (int)status);
            throw new ReleaseFeedException($"GitHub release request failed with HTTP {(int)status}.");
        }

        string json;
        try
        {
            json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException)
        {
            throw new ReleaseFeedException("Reading the GitHub release response failed.", ex);
        }

        return Parse(json);
    }

    private ReleaseInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return MapRelease(document.RootElement);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "GitHub release response was not valid JSON");
            throw new ReleaseFeedException("The GitHub release response was not valid JSON.", ex);
        }
    }

    private ReleaseInfo? MapRelease(JsonElement root)
    {
        if (IsTrue(root, "draft") || IsTrue(root, "prerelease"))
        {
            _logger?.LogInformation("Ignoring a draft or prerelease GitHub release");
            return null;
        }

        var tagName = GetString(root, "tag_name");
        if (tagName is null || !AppVersion.TryParse(tagName, out var version))
        {
            _logger?.LogWarning("Ignoring GitHub release with unparseable tag {Tag}", tagName ?? "(none)");
            return null;
        }

        var htmlUrl = GetString(root, "html_url");
        if (!IsReleasePageUrl(htmlUrl, tagName))
        {
            _logger?.LogWarning("Ignoring GitHub release {Tag}: unexpected release page URL", tagName);
            return null;
        }

        var notes = GetString(root, "body") ?? string.Empty;
        if (notes.Length > MaxNotesLength) notes = notes[..MaxNotesLength];

        var assets = ReadAssets(root);
        var exe = SelectUniqueAsset(assets, AssetName, MaxAssetSize, tagName);
        if (exe is null) return null;

        var sha256 = NormalizeDigest(exe.Value.Digest);
        var checksumAsset = sha256 is null
            ? SelectUniqueAsset(assets, ChecksumAssetName, MaxChecksumAssetSize, tagName)
            : null;

        _logger?.LogInformation("GitHub latest release is {Tag} with asset {AssetName}", tagName, AssetName);

        return new ReleaseInfo(
            version,
            tagName,
            htmlUrl!,
            notes,
            new ReleaseAsset(exe.Value.Name, exe.Value.Url!, exe.Value.Size),
            sha256,
            checksumAsset is null
                ? null
                : new ReleaseAsset(checksumAsset.Value.Name, checksumAsset.Value.Url!, checksumAsset.Value.Size));
    }

    private static bool IsTrue(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static List<RemoteAsset> ReadAssets(JsonElement root)
    {
        var assets = new List<RemoteAsset>();
        if (!root.TryGetProperty("assets", out var array) || array.ValueKind != JsonValueKind.Array) return assets;

        foreach (var item in array.EnumerateArray())
        {
            var name = GetString(item, "name");
            if (string.IsNullOrEmpty(name)) continue;

            Uri? url = null;
            var rawUrl = GetString(item, "browser_download_url");
            if (rawUrl is not null && Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed) && GitHubHosts.IsAllowed(parsed))
            {
                url = parsed;
            }

            long size = 0;
            if (item.TryGetProperty("size", out var sizeElement)
                && sizeElement.ValueKind == JsonValueKind.Number
                && sizeElement.TryGetInt64(out var parsedSize))
            {
                size = parsedSize;
            }

            assets.Add(new RemoteAsset(
                name,
                url,
                size,
                GetString(item, "state"),
                GetString(item, "digest")));
        }

        return assets;
    }

    private RemoteAsset? SelectUniqueAsset(List<RemoteAsset> assets, string name, long maxSize, string tagName)
    {
        RemoteAsset? match = null;
        var count = 0;

        foreach (var asset in assets)
        {
            if (!string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase)) continue;
            if (asset.Url is null) continue;
            if (asset.Size <= 0 || asset.Size > maxSize) continue;

            match = asset;
            count++;
        }

        if (count == 1) return match;

        _logger?.LogWarning(
            "Release {Tag} has {Count} usable {AssetName} assets; expected exactly one",
            tagName,
            count,
            name);
        return null;
    }

    private static string? NormalizeDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;

        const string prefix = "sha256:";
        var value = digest.Trim();
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var hex = value[prefix.Length..];
        if (hex.Length != 64) return null;
        foreach (var c in hex)
        {
            if (!Uri.IsHexDigit(c)) return null;
        }

        return hex.ToLowerInvariant();
    }

    private static bool IsReleasePageUrl(string? htmlUrl, string tagName)
    {
        if (!Uri.TryCreate(htmlUrl, UriKind.Absolute, out var uri)) return false;
        if (!GitHubHosts.IsAllowed(uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;

        var expectedPath = $"/LanternOfDarkness/AiMeter/releases/tag/{tagName}";
        return string.Equals(uri.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)) return false;

        foreach (var value in values)
        {
            if (value.Trim() == "0") return true;
        }

        return false;
    }

    private readonly record struct RemoteAsset(string Name, Uri? Url, long Size, string? State, string? Digest);
}

/// <summary>GitHub rejected the release request because the API rate limit was exhausted.</summary>
public sealed class ReleaseFeedRateLimitedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ReleaseFeedRateLimitedException()
        : base("GitHub release request was rate limited.")
    {
    }
}

/// <summary>The latest-release request failed; the inner exception carries the cause.</summary>
public sealed class ReleaseFeedException : Exception
{
    /// <summary>Creates the exception with a short message.</summary>
    public ReleaseFeedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a short message and the underlying cause.</summary>
    public ReleaseFeedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
