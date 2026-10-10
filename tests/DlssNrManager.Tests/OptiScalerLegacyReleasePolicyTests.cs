using System.Text.Json;
using DlssNrManager.Services;
using DlssNrManager.Models;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class OptiScalerLegacyReleasePolicyTests
{
    private const string Name = "OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip";
    private const string Url =
        "https://github.com/grg914/dlss-nr-manager/releases/download/v3.2.0/" + Name;
    private const string Sha =
        "14aa35affd569579709b2705cfa3c2624971a3d9e8e238fe71139954e23e734b";

    private static JsonDocument Release(
        long releaseId = 406116106,
        long assetId = 620010799,
        string releaseTag = "v3.2.0",
        string assetName = Name,
        string assetUrl = Url,
        string digest = "sha256:" + Sha,
        long size = 131421020,
        bool prerelease = false,
        bool draft = false)
        => JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            id = releaseId,
            tag_name = releaseTag,
            draft,
            prerelease,
            assets = new[]
            {
                new
                {
                    id = assetId,
                    name = assetName,
                    browser_download_url = assetUrl,
                    digest,
                    size
                }
            }
        }));

    [Fact]
    public void Original_v320_metadata_selects_exact_digest_pinned_trial()
    {
        using var doc = Release();
        var candidate = OptiScalerLegacyReleasePolicy.TrySelect(doc.RootElement);
        Assert.NotNull(candidate);
        Assert.Equal("v0.7.7-pre0", candidate!.Tag);
        Assert.False(candidate.Prerelease);
        Assert.Equal(Url, candidate.ZipUrl);
        Assert.Equal(Sha, candidate.ZipSha256);
        Assert.True(OptiScalerLegacyReleasePolicy.IsPinnedCandidate(candidate));
        Assert.Equal(131421020, OptiScalerLegacyReleasePolicy.ArchiveSize);
    }

    [Theory]
    [InlineData("release-id")]
    [InlineData("asset-id")]
    [InlineData("tag")]
    [InlineData("filename")]
    [InlineData("url")]
    [InlineData("sha256")]
    [InlineData("size")]
    [InlineData("draft")]
    [InlineData("prerelease")]
    public void Mutated_provenance_fails_closed_even_if_version_looks_equal(string field)
    {
        using var doc = field switch
        {
            "release-id" => Release(releaseId: 406116107),
            "asset-id" => Release(assetId: 620010800),
            "tag" => Release(releaseTag: "v3.1.1"),
            "filename" => Release(assetName: "OptiScaler-NR-v0.7.7-pre0-other.zip"),
            "url" => Release(assetUrl: Url.Replace("/v3.2.0/", "/runtime-seed-v1/")),
            "sha256" => Release(digest: "sha256:" + new string('0', 64)),
            "size" => Release(size: 131421021),
            "draft" => Release(draft: true),
            _ => Release(prerelease: true)
        };
        Assert.Null(OptiScalerLegacyReleasePolicy.TrySelect(doc.RootElement));
    }

    [Fact]
    public void Same_tag_but_wrong_archive_is_never_the_verified_candidate()
    {
        using var doc = Release();
        var pinned = OptiScalerLegacyReleasePolicy.TrySelect(doc.RootElement)!;
        Assert.False(OptiScalerLegacyReleasePolicy.IsPinnedCandidate(
            pinned with { ZipUrl = Url.Replace("/v3.2.0/", "/v3.1.1/") }));
        Assert.False(OptiScalerLegacyReleasePolicy.IsPinnedCandidate(
            pinned with { ZipSha256 = new string('0', 64) }));
    }

    [Fact]
    public void Missing_or_malformed_release_assets_are_not_accepted()
    {
        using var missing = JsonDocument.Parse("{\"assets\":[]}");
        using var empty = JsonDocument.Parse(
            "{\"id\":406116106,\"tag_name\":\"v3.2.0\",\"assets\":[]}");
        Assert.Null(OptiScalerLegacyReleasePolicy.TrySelect(missing.RootElement));
        Assert.Null(OptiScalerLegacyReleasePolicy.TrySelect(empty.RootElement));
    }
    [Fact]
    public void Per_game_preference_uses_exact_package_url_not_ambiguous_tag()
    {
        var original = new ReleaseInfo(
            OptiScalerLegacyReleasePolicy.Tag,
            "Original",
            false,
            OptiScalerLegacyReleasePolicy.ArchiveUrl,
            OptiScalerLegacyReleasePolicy.ArchiveSha256);
        var sameVersionDifferentArchive = original with
        {
            ZipUrl = "https://github.com/grg914/dlss-nr-manager/releases/download/v3.1.1/OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip"
        };

        Assert.True(GamePreferenceService.MatchesOptiScalerBuild(
            original, original.ZipUrl));
        Assert.False(GamePreferenceService.MatchesOptiScalerBuild(
            sameVersionDifferentArchive, original.ZipUrl));
        Assert.True(GamePreferenceService.MatchesOptiScalerBuild(
            original, original.Tag)); // backwards compatibility for older preferences
        Assert.False(GamePreferenceService.MatchesOptiScalerBuild(
            original, sameVersionDifferentArchive.ZipUrl));
        Assert.False(GamePreferenceService.MatchesOptiScalerBuild(
            original, null));
    }

    [Fact]
    public void Historical_manifest_json_remains_readable_after_provenance_extension()
    {
        var existing = JsonSerializer.Deserialize<InstallManifest>(
            "{\"Release\":\"v0.7.7-pre0\",\"Proxy\":\"dxgi.dll\",\"GameExecutable\":\"game.exe\"," +
            "\"GameExecutableHash\":\"example\",\"RuntimeHash\":\"\",\"InstalledAt\":\"2026-10-08T12:00:00Z\"}");
        Assert.NotNull(existing);
        Assert.Null(existing!.SourceArchiveUrl);
        Assert.Null(existing.SourceArchiveSha256);
    }

}
