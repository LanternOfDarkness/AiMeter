using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiMeter.Services.Updates;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class GithubReleaseFeedTests
{
    private const string Digest = "4c7663d92018bebb2e1a2a9201eb7a1fa96fcabec36e42e2a4c71fbe9b882df0";
    private const string DefaultHtmlUrl = "https://github.com/LanternOfDarkness/AiMeter/releases/tag/v2.1.0";
    private const string DefaultAssetUrl = "https://github.com/LanternOfDarkness/AiMeter/releases/download/v2.1.0/AiMeter.exe";

    private const string LiveJson =
        """
        {"tag_name":"v2.0.2","html_url":"https://github.com/LanternOfDarkness/AiMeter/releases/tag/v2.0.2","body":"notes","draft":false,"prerelease":false,"assets":[{"name":"AiMeter.exe","state":"uploaded","size":31461672,"digest":"sha256:4c7663d92018bebb2e1a2a9201eb7a1fa96fcabec36e42e2a4c71fbe9b882df0","browser_download_url":"https://github.com/LanternOfDarkness/AiMeter/releases/download/v2.0.2/AiMeter.exe"}]}
        """;

    [Fact]
    public async Task Parses_the_live_release_payload()
    {
        var release = await GetReleaseAsync(LiveJson);

        release.Should().NotBeNull();
        release!.Version.Should().Be(new AppVersion(2, 0, 2));
        release.TagName.Should().Be("v2.0.2");
        release.HtmlUrl.Should().Be("https://github.com/LanternOfDarkness/AiMeter/releases/tag/v2.0.2");
        release.Notes.Should().Be("notes");
        release.Asset.Name.Should().Be("AiMeter.exe");
        release.Asset.DownloadUrl.Should().Be(new Uri(DefaultAssetUrl.Replace("v2.1.0", "v2.0.2")));
        release.Asset.Size.Should().Be(31461672);
        release.Sha256.Should().Be(Digest);
        release.ChecksumAsset.Should().BeNull();
    }

    [Fact]
    public async Task Selects_the_exe_asset_case_insensitively()
    {
        var json = ReleaseJson(
        [
            Asset("notes.zip", url: "https://github.com/LanternOfDarkness/AiMeter/releases/download/v2.1.0/notes.zip"),
            Asset("aimeter.EXE"),
        ]);

        var release = await GetReleaseAsync(json);

        release.Should().NotBeNull();
        release!.Asset.Name.Should().Be("aimeter.EXE");
    }

    [Fact]
    public async Task Rejects_duplicate_exe_assets()
    {
        var release = await GetReleaseAsync(ReleaseJson([Asset("AiMeter.exe"), Asset("AiMeter.exe")]));

        release.Should().BeNull();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Rejects_draft_and_prerelease_payloads(bool draft, bool prerelease)
    {
        var release = await GetReleaseAsync(ReleaseJson([Asset("AiMeter.exe")], draft: draft, prerelease: prerelease));

        release.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_the_exe_asset_is_missing()
    {
        var release = await GetReleaseAsync(ReleaseJson(Asset("other.exe")));

        release.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_the_exe_asset_is_oversized()
    {
        var release = await GetReleaseAsync(ReleaseJson(Asset("AiMeter.exe", size: 200L * 1024 * 1024 + 1)));

        release.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Returns_null_when_the_exe_asset_size_is_invalid(long size)
    {
        var release = await GetReleaseAsync(ReleaseJson(Asset("AiMeter.exe", size: size)));

        release.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_the_exe_asset_is_not_uploaded()
    {
        var release = await GetReleaseAsync(ReleaseJson(Asset("AiMeter.exe", state: "starter")));

        release.Should().BeNull();
    }

    [Theory]
    [InlineData("SHA256:4C7663D92018BEBB2E1A2A9201EB7A1FA96FCABEC36E42E2A4C71FBE9B882DF0")]
    [InlineData("sha256:4c7663d92018bebb2e1a2a9201eb7a1fa96fcabec36e42e2a4c71fbe9b882df0")]
    public async Task Normalizes_digests_to_lowercase_hex(string digest)
    {
        var release = await GetReleaseAsync(ReleaseJson(Asset("AiMeter.exe", digest: digest)));

        release.Should().NotBeNull();
        release!.Sha256.Should().Be(Digest);
    }

    [Theory]
    [InlineData("sha512:4c7663d92018bebb2e1a2a9201eb7a1fa96fcabec36e42e2a4c71fbe9b882df0")]
    [InlineData("sha256:not-hex")]
    [InlineData("4c7663d92018bebb2e1a2a9201eb7a1fa96fcabec36e42e2a4c71fbe9b882df0")]
    [InlineData(null)]
    public async Task Falls_back_to_the_sha256_asset_when_the_digest_is_absent_or_malformed(string? digest)
    {
        var checksumUrl = "https://github.com/LanternOfDarkness/AiMeter/releases/download/v2.1.0/AiMeter.exe.sha256";
        var json = ReleaseJson(
        [
            Asset("AiMeter.exe", digest: digest),
            Asset("AiMeter.exe.sha256", size: 64, digest: null, url: checksumUrl),
        ]);

        var release = await GetReleaseAsync(json);

        release.Should().NotBeNull();
        release!.Sha256.Should().BeNull();
        release.ChecksumAsset.Should().NotBeNull();
        release.ChecksumAsset!.Name.Should().Be("AiMeter.exe.sha256");
        release.ChecksumAsset.DownloadUrl.Should().Be(new Uri(checksumUrl));
        release.ChecksumAsset.Size.Should().Be(64);
    }

    [Fact]
    public async Task Leaves_both_checksums_null_when_the_sha256_asset_is_absent()
    {
        var release = await GetReleaseAsync(ReleaseJson(Asset("AiMeter.exe", digest: null)));

        release.Should().NotBeNull();
        release!.Sha256.Should().BeNull();
        release.ChecksumAsset.Should().BeNull();
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("v2.1")]
    [InlineData("v2.1.0.0")]
    public async Task Returns_null_for_an_unparseable_tag(string tag)
    {
        var release = await GetReleaseAsync(ReleaseJson([Asset("AiMeter.exe")], tag: tag));

        release.Should().BeNull();
    }

    [Fact]
    public async Task Accepts_a_release_page_url_with_a_query_string()
    {
        var release = await GetReleaseAsync(
            ReleaseJson([Asset("AiMeter.exe")], htmlUrl: DefaultHtmlUrl + "?utm_source=test"));

        release.Should().NotBeNull();
    }

    [Theory]
    [InlineData("https://evil.example/LanternOfDarkness/AiMeter/releases/tag/v2.1.0")]
    [InlineData("https://github.com.evil.example/LanternOfDarkness/AiMeter/releases/tag/v2.1.0")]
    [InlineData("http://github.com/LanternOfDarkness/AiMeter/releases/tag/v2.1.0")]
    [InlineData("https://github.com/LanternOfDarkness/AiMeter/releases/tag/v2.1.1")]
    [InlineData("https://github.com/LanternOfDarkness/AiMeter/releases")]
    [InlineData("https://user:pass@github.com/LanternOfDarkness/AiMeter/releases/tag/v2.1.0")]
    public async Task Returns_null_for_an_untrustworthy_release_page_url(string htmlUrl)
    {
        var release = await GetReleaseAsync(ReleaseJson([Asset("AiMeter.exe")], htmlUrl: htmlUrl));

        release.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_the_asset_url_is_not_an_allowed_host()
    {
        var release = await GetReleaseAsync(
            ReleaseJson(Asset("AiMeter.exe", url: "https://evil.example/AiMeter.exe")));

        release.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_for_404()
    {
        var release = await GetReleaseAsync(HttpStatusCode.NotFound);

        release.Should().BeNull();
    }

    [Fact]
    public async Task Throws_a_typed_exception_for_a_403_rate_limit()
    {
        var handler = new FakeHandler(HttpStatusCode.Forbidden, _ =>
            _.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0"));
        using var feed = new GithubReleaseFeed(handler);

        var act = async () => await feed.GetLatestAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ReleaseFeedRateLimitedException>();
    }

    [Fact]
    public async Task Throws_a_typed_exception_for_a_429_rate_limit()
    {
        var handler = new FakeHandler(HttpStatusCode.TooManyRequests, _ =>
            _.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0"));
        using var feed = new GithubReleaseFeed(handler);

        var act = async () => await feed.GetLatestAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ReleaseFeedRateLimitedException>();
    }

    [Fact]
    public async Task Throws_a_typed_exception_for_other_failures()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError);
        using var feed = new GithubReleaseFeed(handler);

        var act = async () => await feed.GetLatestAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ReleaseFeedException>();
    }

    [Fact]
    public async Task Wraps_malformed_json_in_a_typed_exception()
    {
        var act = async () => await GetReleaseAsync("{ not json");

        await act.Should().ThrowAsync<ReleaseFeedException>();
    }

    [Fact]
    public async Task Sends_the_pinned_headers()
    {
        HttpRequestMessage? captured = null;
        await GetReleaseAsync(LiveJson, request => captured = request);

        captured.Should().NotBeNull();
        captured!.RequestUri.Should().Be(new Uri("https://api.github.com/repos/LanternOfDarkness/AiMeter/releases/latest"));
        captured.Headers.UserAgent.ToString().Should().Be("AiMeter/" + AppInfo.Current);
        captured.Headers.Accept.ToString().Should().Be("application/vnd.github+json");
        captured.Headers.GetValues("X-GitHub-Api-Version").Should().ContainSingle().Which.Should().Be("2022-11-28");
    }

    private static async Task<ReleaseInfo?> GetReleaseAsync(string json, Action<HttpRequestMessage>? inspect = null)
        => await GetReleaseAsync(HttpStatusCode.OK, json, inspect: inspect);

    private static async Task<ReleaseInfo?> GetReleaseAsync(
        HttpStatusCode status,
        string? json = null,
        Action<HttpResponseMessage>? configure = null,
        Action<HttpRequestMessage>? inspect = null)
    {
        var handler = new FakeHandler(status, configure, inspect, json);
        using var feed = new GithubReleaseFeed(handler);
        return await feed.GetLatestAsync(CancellationToken.None);
    }

    private static string ReleaseJson(
        object asset,
        string tag = "v2.1.0",
        string? htmlUrl = null,
        bool draft = false,
        bool prerelease = false)
        => ReleaseJson([asset], tag, htmlUrl, draft, prerelease);

    private static string ReleaseJson(
        object[] assets,
        string tag = "v2.1.0",
        string? htmlUrl = null,
        bool draft = false,
        bool prerelease = false)
    {
        return JsonSerializer.Serialize(new
        {
            tag_name = tag,
            html_url = htmlUrl ?? DefaultHtmlUrl,
            body = "notes",
            draft,
            prerelease,
            assets,
        });
    }

    private static object Asset(
        string name,
        long size = 31461672,
        string state = "uploaded",
        string? digest = Digest,
        string? url = null)
    {
        return new
        {
            name,
            state,
            size,
            digest,
            browser_download_url = url
                ?? "https://github.com/LanternOfDarkness/AiMeter/releases/download/v2.1.0/" + name,
        };
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        private readonly Action<HttpRequestMessage>? _inspect;

        public FakeHandler(
            HttpStatusCode status,
            Action<HttpResponseMessage>? configure = null,
            Action<HttpRequestMessage>? inspect = null,
            string? body = null)
        {
            _response = new HttpResponseMessage(status);
            if (body is not null)
            {
                _response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            configure?.Invoke(_response);
            _inspect = inspect;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _inspect?.Invoke(request);
            return Task.FromResult(_response);
        }
    }
}
