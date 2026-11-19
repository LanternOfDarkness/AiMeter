using System;

namespace AiMeter.Services.Updates;

/// <summary>A downloadable file attached to a GitHub release.</summary>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);

/// <summary>
/// The subset of a GitHub release that the update flow needs.
/// </summary>
/// <param name="Version">Tag parsed as a semver.</param>
/// <param name="TagName">Raw tag name.</param>
/// <param name="HtmlUrl">The human release page, used by every fallback path.</param>
/// <param name="Notes">Release body for the UI; the feed truncates it to 8000 chars.</param>
/// <param name="Asset">The <c>AiMeter.exe</c> asset to download.</param>
/// <param name="Sha256">
/// Normalized 64-char lowercase hex from GitHub's asset digest, or <see langword="null"/>
/// when the API does not provide one.
/// </param>
/// <param name="ChecksumAsset">
/// The <c>AiMeter.exe.sha256</c> upload, used only when no digest is available.
/// </param>
public sealed record ReleaseInfo(
    AppVersion Version,
    string TagName,
    string HtmlUrl,
    string Notes,
    ReleaseAsset Asset,
    string? Sha256,
    ReleaseAsset? ChecksumAsset);
