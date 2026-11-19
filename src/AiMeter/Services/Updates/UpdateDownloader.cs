using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AiMeter.Services.Updates;

/// <summary>Progress of the update asset download.</summary>
/// <param name="BytesReceived">Bytes written to the staging file so far.</param>
/// <param name="TotalBytes">The final response's Content-Length, or <see langword="null"/> when absent.</param>
/// <param name="Fraction">Received over total, or <see langword="null"/> when the total is unknown.</param>
public sealed record DownloadProgress(long BytesReceived, long? TotalBytes, double? Fraction);

/// <summary>Downloads a release asset to the staging directory and verifies it.</summary>
public interface IUpdateDownloader
{
    /// <summary>
    /// Streams the release asset into <paramref name="destinationDirectory"/>, verifies its
    /// SHA-256 and embedded version, and returns the absolute staged <c>.exe</c> path.
    /// </summary>
    Task<string> DownloadAsync(
        ReleaseInfo release,
        string destinationDirectory,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct);

    /// <summary>Deletes staged download files older than 7 days in the default staging directory; returns how many were removed. Never throws.</summary>
    int CleanupStagedFiles();
}

/// <summary>A network, IO or checksum-availability failure while downloading an update.</summary>
public sealed class UpdateDownloadException : Exception
{
    /// <summary>Creates the exception with a user-facing message.</summary>
    public UpdateDownloadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a user-facing message and the underlying cause.</summary>
    public UpdateDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The downloaded file does not match the published hash, size or version.</summary>
public sealed class UpdateIntegrityException : Exception
{
    /// <summary>Creates the exception with a user-facing message.</summary>
    public UpdateIntegrityException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a user-facing message and the underlying cause.</summary>
    public UpdateIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Downloads the release executable over a manually followed, host-validated redirect chain,
/// hashes it while streaming, and stages it as <c>.exe</c> only after the hash and embedded
/// version both check out.
/// </summary>
public sealed class UpdateDownloader : IUpdateDownloader, IDisposable
{
    /// <summary>Hard cap on a downloaded update, independent of the release metadata.</summary>
    public const long MaximumDownloadBytes = 200L * 1024 * 1024;

    private const int BufferSize = 81_920;
    private const int MaximumRedirects = 5;
    private const int MaximumChecksumBytes = 1024;
    private const string StagingPrefix = "AiMeter-";
    private const string PartialExtension = ".part";
    private const string ExecutableExtension = ".exe";
    private const string UserAgentProduct = "AiMeter";

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultDownloadBudget = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StagedFileMaxAge = TimeSpan.FromDays(7);

    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateDownloader>? _logger;
    private readonly string _stagingDirectory;
    private readonly TimeSpan _downloadBudget;

    /// <summary>Creates a downloader that writes to the default staging directory.</summary>
    public UpdateDownloader(ILogger<UpdateDownloader>? logger = null)
        : this(new HttpClientHandler { AllowAutoRedirect = false }, logger, null)
    {
    }

    /// <summary>Creates a downloader with an injected transport, for tests.</summary>
    public UpdateDownloader(
        HttpMessageHandler handler,
        ILogger<UpdateDownloader>? logger = null,
        string? stagingDirectory = null,
        TimeSpan? downloadBudget = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _httpClient = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _logger = logger;
        _downloadBudget = downloadBudget ?? DefaultDownloadBudget;
        _stagingDirectory = string.IsNullOrWhiteSpace(stagingDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AiMeter",
                "updates")
            : Path.GetFullPath(stagingDirectory);
    }

    /// <inheritdoc />
    public async Task<string> DownloadAsync(
        ReleaseInfo release,
        string destinationDirectory,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(release);
        ct.ThrowIfCancellationRequested();

        var directory = string.IsNullOrWhiteSpace(destinationDirectory)
            ? _stagingDirectory
            : Path.GetFullPath(destinationDirectory);

        var fileName =
            $"{StagingPrefix}{release.Version}-{Environment.ProcessId}-{Guid.NewGuid():N}{ExecutableExtension}{PartialExtension}";
        var partialPath = Path.Combine(directory, fileName);
        var stagedPath = partialPath[..^PartialExtension.Length];

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_downloadBudget);
        var token = budget.Token;

        try
        {
            Directory.CreateDirectory(directory);
            var expectedHash = await ResolveExpectedHashAsync(release, token).ConfigureAwait(false);

            string actualHash;
            long? totalBytes;

            await using (var file = new FileStream(
                             partialPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var response = await SendWithRedirectsAsync(release.Asset.DownloadUrl, token)
                    .ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new UpdateDownloadException(
                        $"The update download failed with HTTP {(int)response.StatusCode}.");
                }

                totalBytes = response.Content.Headers.ContentLength;
                if (totalBytes.HasValue && totalBytes.Value != release.Asset.Size)
                {
                    throw new UpdateDownloadException(
                        $"The update server reported {totalBytes.Value} bytes but the release lists {release.Asset.Size}.");
                }

                actualHash = await CopyAndHashAsync(response, file, release.Asset.Size, totalBytes, progress, token)
                    .ConfigureAwait(false);
            }

            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateIntegrityException(
                    "The downloaded update does not match the published SHA-256 checksum.");
            }

            VerifyProductVersion(partialPath, release.Version);

            File.Move(partialPath, stagedPath);
            _logger?.LogInformation(
                "Staged verified AiMeter {Version} update at {Path}.",
                release.Version,
                stagedPath);
            return Path.GetFullPath(stagedPath);
        }
        catch (Exception ex)
        {
            TryDelete(partialPath);
            TryDelete(stagedPath);

            if (ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                throw new UpdateDownloadException("The update download timed out.", ex);
            }

            if (ex is OperationCanceledException or UpdateDownloadException or UpdateIntegrityException)
            {
                throw;
            }

            throw new UpdateDownloadException("The update download failed: " + ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public int CleanupStagedFiles()
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(_stagingDirectory)) return 0;

            var cutoff = DateTime.UtcNow - StagedFileMaxAge;
            foreach (var path in Directory.EnumerateFiles(_stagingDirectory))
            {
                var name = Path.GetFileName(path);
                var isPartial = name.EndsWith(PartialExtension, StringComparison.OrdinalIgnoreCase);
                var isStaged = name.StartsWith(StagingPrefix, StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase);
                if (!isPartial && !isStaged) continue;

                try
                {
                    if (File.GetLastWriteTimeUtc(path) >= cutoff) continue;

                    File.Delete(path);
                    removed++;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Could not delete staged update file {Path}.", path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not inspect the update staging directory {Directory}.", _stagingDirectory);
        }

        return removed;
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();

    private async Task<string> ResolveExpectedHashAsync(ReleaseInfo release, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(release.Sha256))
        {
            return NormalizeHash(release.Sha256)
                ?? throw new UpdateDownloadException("The release publishes a malformed checksum.");
        }

        if (release.ChecksumAsset is null)
        {
            throw new UpdateDownloadException(
                "This release has no checksum; install it from the release page.");
        }

        using var response = await SendWithRedirectsAsync(release.ChecksumAsset.DownloadUrl, ct)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new UpdateDownloadException(
                $"The checksum download failed with HTTP {(int)response.StatusCode}.");
        }

        var buffer = new byte[MaximumChecksumBytes];
        var length = 0;
        await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
                if (read == 0) break;

                length += read;
            }
        }

        var text = Encoding.UTF8.GetString(buffer, 0, length).Trim('\uFEFF', '\r', '\n', ' ', '\t');
        var token = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token is null || !IsHex64(token))
        {
            throw new UpdateDownloadException("The release does not publish a usable checksum.");
        }

        return token;
    }

    private async Task<string> CopyAndHashAsync(
        HttpResponseMessage response,
        FileStream file,
        long assetSize,
        long? totalBytes,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long received = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.MinValue;

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = await source.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;

            received += read;
            if (received > assetSize || received > MaximumDownloadBytes)
            {
                throw new UpdateDownloadException(
                    "The update is larger than the size published with the release.");
            }

            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);

            if (progress is not null)
            {
                var elapsed = stopwatch.Elapsed;
                if (lastReport == TimeSpan.MinValue || elapsed - lastReport >= ProgressInterval)
                {
                    lastReport = elapsed;
                    progress.Report(CreateProgress(received, totalBytes));
                }
            }
        }

        progress?.Report(CreateProgress(received, totalBytes));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private async Task<HttpResponseMessage> SendWithRedirectsAsync(Uri initialUri, CancellationToken ct)
    {
        var current = initialUri;
        for (var hop = 0; ; hop++)
        {
            EnsureAllowedHost(current);

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd($"{UserAgentProduct}/{AppInfo.Current}");

            var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode)) return response;

            var location = response.Headers.Location;
            response.Dispose();

            if (location is null)
            {
                throw new UpdateDownloadException("The update server redirected without a location.");
            }

            if (hop >= MaximumRedirects)
            {
                throw new UpdateDownloadException("Too many redirects while downloading the update.");
            }

            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            _logger?.LogDebug("Following an update redirect to {Host}.", next.Host);
            current = next;
        }
    }

    private static void EnsureAllowedHost(Uri uri)
    {
#if DEBUG
        if (!GitHubHosts.IsAllowed(uri) && !IsLocalDevelopmentUri(uri))
#else
        if (!GitHubHosts.IsAllowed(uri))
#endif
        {
            throw new UpdateDownloadException(
                $"The update download host {uri.Host} is not allowed.");
        }
    }

#if DEBUG
    private static bool IsLocalDevelopmentUri(Uri uri) =>
        uri is { IsAbsoluteUri: true }
        && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.IsDefaultPort
        && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase));
#endif

    private static void VerifyProductVersion(string path, AppVersion releaseVersion)
    {
        string? productVersion;
        try
        {
            productVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion;
        }
        catch (Exception ex)
        {
            throw new UpdateIntegrityException(
                "The downloaded update does not expose a readable version.",
                ex);
        }

        var normalized = AppInfo.NormalizeInformationalVersion(productVersion);
        if (normalized is null
            || !AppVersion.TryParse(normalized, out var version)
            || version.CompareTo(releaseVersion) < 0)
        {
            throw new UpdateIntegrityException(
                $"The downloaded update's version does not match the published release {releaseVersion}.");
        }
    }

    private static DownloadProgress CreateProgress(long received, long? totalBytes)
    {
        double? fraction = totalBytes is > 0
            ? Math.Clamp((double)received / totalBytes.Value, 0d, 1d)
            : null;
        return new DownloadProgress(received, totalBytes, fraction);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private static string? NormalizeHash(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["sha256:".Length..];
        }

        return IsHex64(trimmed) ? trimmed : null;
    }

    private static bool IsHex64(string value)
    {
        if (value.Length != 64) return false;

        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }

        return true;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not delete staged file {Path}.", path);
        }
    }
}
