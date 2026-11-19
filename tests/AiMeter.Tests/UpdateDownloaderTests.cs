using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiMeter.Services.Updates;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiMeter.Tests;

public class UpdateDownloaderTests
{
    private const string OriginalUrl =
        "https://github.com/LanternOfDarkness/AiMeter/releases/download/v2.1.0/AiMeter.exe";

    private const string RedirectUrl =
        "https://release-assets.githubusercontent.com/github-production-release-asset/1/2?X-Amz-Signature=abc";

    private const string SecondRedirectUrl =
        "https://release-assets.githubusercontent.com/github-production-release-asset/1/2?X-Amz-Signature=def";

    private const string ChecksumUrl =
        "https://github.com/LanternOfDarkness/AiMeter/releases/download/v2.1.0/AiMeter.exe.sha256";

    private const int PayloadChunk = 81_920;

    private static readonly byte[] RealExecutableBytes =
        File.ReadAllBytes(typeof(UpdateDownloader).Assembly.Location);

    private static readonly string RealExecutableHash = Sha256(RealExecutableBytes);

    public static IEnumerable<object[]> ChecksumFormats
    {
        get
        {
            yield return [ "\uFEFF" + RealExecutableHash + "\r\n" ];
            yield return [ RealExecutableHash.ToUpperInvariant() + "  AiMeter.exe\r\n" ];
            yield return [ RealExecutableHash + " AiMeter.exe" ];
        }
    }

    [Fact]
    public async Task Download_streams_the_asset_into_a_unique_staged_exe()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler().Enqueue(Ok(RealExecutableBytes));
            using var downloader = NewDownloader(handler, directory);

            var path = await downloader.DownloadAsync(
                NewRelease(RealExecutableHash, RealExecutableBytes.Length),
                directory,
                progress: null,
                CancellationToken.None);

            path.Should().Be(Path.Combine(directory, Path.GetFileName(path)));
            path.Should().NotEndWith(".part");
            File.ReadAllBytes(path).Should().Equal(RealExecutableBytes);
            Directory.GetFiles(directory, "*.part").Should().BeEmpty();
            handler.Requests.Should().ContainSingle().Which.Should().Be(new Uri(OriginalUrl));
            handler.UserAgents.Single().Should().StartWith("AiMeter/");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Progress_uses_the_final_content_length()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler().Enqueue(Ok(RealExecutableBytes));
            var reports = new List<DownloadProgress>();
            using var downloader = NewDownloader(handler, directory);

            await downloader.DownloadAsync(
                NewRelease(RealExecutableHash, RealExecutableBytes.Length),
                directory,
                new InlineProgress(reports.Add),
                CancellationToken.None);

            reports.Should().NotBeEmpty();
            reports.Should().OnlyContain(report => report.TotalBytes == RealExecutableBytes.Length);
            reports.Should().BeInAscendingOrder(report => report.BytesReceived);
            reports.Last().BytesReceived.Should().Be(RealExecutableBytes.Length);
            reports.Last().Fraction.Should().Be(1.0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Progress_has_no_total_when_the_final_response_is_chunked()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler().Enqueue(ChunkedOk(RealExecutableBytes));
            var reports = new List<DownloadProgress>();
            using var downloader = NewDownloader(handler, directory);

            await downloader.DownloadAsync(
                NewRelease(RealExecutableHash, RealExecutableBytes.Length),
                directory,
                new InlineProgress(reports.Add),
                CancellationToken.None);

            reports.Should().NotBeEmpty();
            reports.Should().OnlyContain(report => report.TotalBytes == null && report.Fraction == null);
            reports.Last().BytesReceived.Should().Be(RealExecutableBytes.Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Redirect_hop_with_zero_length_is_followed_and_the_real_length_wins()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler()
                .Enqueue(Redirect(RedirectUrl))
                .Enqueue(Ok(RealExecutableBytes));
            var reports = new List<DownloadProgress>();
            using var downloader = NewDownloader(handler, directory);

            await downloader.DownloadAsync(
                NewRelease(RealExecutableHash, RealExecutableBytes.Length),
                directory,
                new InlineProgress(reports.Add),
                CancellationToken.None);

            handler.Requests.Select(uri => uri.ToString()).Should().Equal(OriginalUrl, RedirectUrl);
            reports.Last().TotalBytes.Should().Be(RealExecutableBytes.Length);
            reports.Last().Fraction.Should().Be(1.0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Redirect_to_a_disallowed_host_is_rejected()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler()
                .Enqueue(Redirect("https://evil.example.invalid/AiMeter.exe"));
            using var downloader = NewDownloader(handler, directory);

            var act = () => downloader.DownloadAsync(
                NewRelease(RealExecutableHash, RealExecutableBytes.Length),
                directory,
                progress: null,
                CancellationToken.None);

            await act.Should().ThrowAsync<UpdateDownloadException>().WithMessage("*not allowed*");
            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Final_content_length_that_differs_from_the_asset_size_is_rejected()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler().Enqueue(Ok(RealExecutableBytes));
            using var downloader = NewDownloader(handler, directory);

            var act = () => downloader.DownloadAsync(
                NewRelease(RealExecutableHash, RealExecutableBytes.Length + 5),
                directory,
                progress: null,
                CancellationToken.None);

            await act.Should().ThrowAsync<UpdateDownloadException>();
            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Hash_mismatch_deletes_the_staged_files()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler().Enqueue(Ok(RealExecutableBytes));
            using var downloader = NewDownloader(handler, directory);

            var act = () => downloader.DownloadAsync(
                NewRelease(new string('0', 64), RealExecutableBytes.Length),
                directory,
                progress: null,
                CancellationToken.None);

            await act.Should().ThrowAsync<UpdateIntegrityException>();
            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Product_version_below_the_tag_deletes_the_staged_files()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler().Enqueue(Ok(RealExecutableBytes));
            using var downloader = NewDownloader(handler, directory);

            var act = () => downloader.DownloadAsync(
                NewRelease(RealExecutableHash, RealExecutableBytes.Length, version: new AppVersion(99, 0, 0)),
                directory,
                progress: null,
                CancellationToken.None);

            await act.Should().ThrowAsync<UpdateIntegrityException>();
            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Product_version_at_or_above_the_tag_is_accepted()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler().Enqueue(Ok(RealExecutableBytes));
            using var downloader = NewDownloader(handler, directory);

            var path = await downloader.DownloadAsync(
                NewRelease(RealExecutableHash, RealExecutableBytes.Length, version: new AppVersion(0, 0, 1)),
                directory,
                progress: null,
                CancellationToken.None);

            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_deletes_the_partial_file()
    {
        var directory = NewTempDirectory();
        try
        {
            var payload = new byte[PayloadChunk * 3];
            new Random(7).NextBytes(payload);
            var handler = new ScriptedHandler().Enqueue(Ok(payload));
            using var downloader = NewDownloader(handler, directory);
            using var cts = new CancellationTokenSource();
            var progress = new InlineProgress(_ => cts.Cancel());

            var act = () => downloader.DownloadAsync(
                NewRelease(Sha256(payload), payload.Length),
                directory,
                progress,
                cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Two_downloads_use_distinct_staging_files()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler()
                .Enqueue(Ok(RealExecutableBytes))
                .Enqueue(Ok(RealExecutableBytes));
            using var downloader = NewDownloader(handler, directory);
            var release = NewRelease(RealExecutableHash, RealExecutableBytes.Length);

            var first = await downloader.DownloadAsync(release, directory, progress: null, CancellationToken.None);
            var second = await downloader.DownloadAsync(release, directory, progress: null, CancellationToken.None);

            first.Should().NotBe(second);
            File.Exists(first).Should().BeTrue();
            File.Exists(second).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Every_attempt_re_requests_the_original_url()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler()
                .Enqueue(Redirect(RedirectUrl))
                .Enqueue(Ok(RealExecutableBytes))
                .Enqueue(Redirect(SecondRedirectUrl))
                .Enqueue(Ok(RealExecutableBytes));
            using var downloader = NewDownloader(handler, directory);

            var failed = () => downloader.DownloadAsync(
                NewRelease(new string('a', 64), RealExecutableBytes.Length),
                directory,
                progress: null,
                CancellationToken.None);
            await failed.Should().ThrowAsync<UpdateIntegrityException>();

            await downloader.DownloadAsync(
                NewRelease(RealExecutableHash, RealExecutableBytes.Length),
                directory,
                progress: null,
                CancellationToken.None);

            handler.Requests.Select(uri => uri.ToString()).Should().Equal(
                OriginalUrl,
                RedirectUrl,
                OriginalUrl,
                SecondRedirectUrl);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(ChecksumFormats))]
    public async Task Checksum_asset_is_parsed(string checksumText)
    {
        var directory = NewTempDirectory();
        try
        {
            var checksumAsset = new ReleaseAsset(
                "AiMeter.exe.sha256",
                new Uri(ChecksumUrl),
                Encoding.UTF8.GetByteCount(checksumText));
            var handler = new ScriptedHandler()
                .Enqueue(Ok(Encoding.UTF8.GetBytes(checksumText)))
                .Enqueue(Ok(RealExecutableBytes));
            using var downloader = NewDownloader(handler, directory);

            var path = await downloader.DownloadAsync(
                NewRelease(sha256: null, RealExecutableBytes.Length, checksumAsset),
                directory,
                progress: null,
                CancellationToken.None);

            handler.Requests[0].Should().Be(new Uri(ChecksumUrl));
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_checksum_fails_before_writing_any_bytes()
    {
        var directory = NewTempDirectory();
        try
        {
            var handler = new ScriptedHandler();
            using var downloader = NewDownloader(handler, directory);

            var act = () => downloader.DownloadAsync(
                NewRelease(sha256: null, RealExecutableBytes.Length),
                directory,
                progress: null,
                CancellationToken.None);

            await act.Should().ThrowAsync<UpdateDownloadException>()
                .WithMessage("This release has no checksum; install it from the release page.");
            handler.Requests.Should().BeEmpty();
            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static UpdateDownloader NewDownloader(ScriptedHandler handler, string stagingDirectory) =>
        new(handler, NullLogger<UpdateDownloader>.Instance, stagingDirectory);

    private static ReleaseInfo NewRelease(
        string? sha256,
        long size,
        ReleaseAsset? checksumAsset = null,
        AppVersion? version = null) =>
        new(
            version ?? new AppVersion(2, 1, 0),
            "v2.1.0",
            "https://github.com/LanternOfDarkness/AiMeter/releases/tag/v2.1.0",
            string.Empty,
            new ReleaseAsset("AiMeter.exe", new Uri(OriginalUrl), size),
            sha256,
            checksumAsset);

    private static string NewTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "AiMeterDownloaderTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static HttpResponseMessage Ok(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage ChunkedOk(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new UnknownLengthContent(body) };

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri(location) },
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };

    private sealed class InlineProgress : IProgress<DownloadProgress>
    {
        private readonly Action<DownloadProgress> _onReport;

        public InlineProgress(Action<DownloadProgress> onReport) => _onReport = onReport;

        public void Report(DownloadProgress value) => _onReport(value);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public List<Uri> Requests { get; } = [];

        public List<string> UserAgents { get; } = [];

        public ScriptedHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _responses.Enqueue(response);
            return this;
        }

        public ScriptedHandler Enqueue(HttpResponseMessage response) =>
            Enqueue(_ => response);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            UserAgents.Add(request.Headers.UserAgent.ToString());

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("The test handler has no scripted response left.");
            }

            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;

        public UnknownLengthContent(byte[] bytes) => _bytes = bytes;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_bytes, 0, _bytes.Length);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken) =>
            stream.WriteAsync(_bytes.AsMemory(), cancellationToken).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
