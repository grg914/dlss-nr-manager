using System.Text.Json;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class ManagerUpdateAssetPolicyTests
{
    private const string ValidHash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:")]
    [InlineData("sha256:abc123")]
    [InlineData("sha512:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    public void Missing_or_invalid_digest_never_offers_an_update(string? digest)
    {
        using var release = MakeRelease(("DlssNrManager.exe", digest));
        Assert.Null(ManagerUpdateAssetPolicy.SelectRelease(release.RootElement));
    }

    [Theory]
    [InlineData("DlssNrManager-setup.exe")]
    [InlineData("unrelated.exe")]
    [InlineData("unrelated.zip")]
    [InlineData("DlssNrManager-win-x86.zip")]
    public void Other_release_executables_and_archives_cannot_be_substituted(string name)
    {
        using var release = MakeRelease((name, "sha256:" + ValidHash));
        Assert.Null(ManagerUpdateAssetPolicy.SelectRelease(release.RootElement));
    }

    [Fact]
    public void Canonical_executable_with_valid_digest_is_preferred_to_zip()
    {
        using var release = MakeRelease(
            ("DlssNrManager-win-x64.zip", "sha256:" + ValidHash),
            ("DlssNrManager.exe", "SHA256:" + ValidHash.ToUpperInvariant()));
        var selected = ManagerUpdateAssetPolicy.SelectRelease(release.RootElement);
        Assert.NotNull(selected);
        Assert.Equal("DlssNrManager.exe", selected.AssetName);
        Assert.Equal(ValidHash.ToUpperInvariant(), selected.Sha256);
    }

    [Fact]
    public void Canonical_zip_can_be_used_when_exe_has_no_verifiable_digest()
    {
        using var release = MakeRelease(
            ("DlssNrManager.exe", null),
            ("DlssNrManager-win-x64.zip", "sha256:" + ValidHash));
        Assert.Equal("DlssNrManager-win-x64.zip",
            ManagerUpdateAssetPolicy.SelectRelease(release.RootElement)?.AssetName);
    }

    [Fact]
    public void Invalid_release_metadata_is_not_an_update()
    {
        using var release = JsonDocument.Parse(
            "{\"tag_name\":\"not-a-version\",\"html_url\":\"https://github.com\",\"assets\":[]}");
        Assert.Null(ManagerUpdateAssetPolicy.SelectRelease(release.RootElement));
    }

    [Fact]
    public void Strict_update_hash_guard_requires_exactly_64_hex_characters()
    {
        Assert.False(ManagerUpdateAssetPolicy.IsValidSha256(null));
        Assert.False(ManagerUpdateAssetPolicy.IsValidSha256(ValidHash[..63]));
        Assert.False(ManagerUpdateAssetPolicy.IsValidSha256(ValidHash + "0"));
        Assert.True(ManagerUpdateAssetPolicy.IsValidSha256(ValidHash));
    }

    [Fact]
    public void Staging_must_verify_hash_even_when_release_metadata_was_supplied_manually()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "Services", "AppUpdateService.cs"));
        var guard = source.IndexOf("ManagerUpdateAssetPolicy.IsValidSha256(release.Sha256)",
            StringComparison.Ordinal);
        var prepare = source.IndexOf("Directory.CreateDirectory(root);",
            StringComparison.Ordinal);
        var compare = source.IndexOf("actual.Equals(release.Sha256,",
            StringComparison.Ordinal);

        Assert.True(guard >= 0 && guard < prepare);
        Assert.True(compare > prepare);
        Assert.DoesNotContain("if (!string.IsNullOrWhiteSpace(release.Sha256))", source);
    }

    private static JsonDocument MakeRelease(params (string Name, string? Digest)[] assets) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            tag_name = "v4.0.0",
            html_url = "https://github.com/grg914/dlss-nr-manager/releases/tag/v4.0.0",
            assets = assets.Select(x => new
            {
                name = x.Name,
                browser_download_url =
                    "https://github.com/grg914/dlss-nr-manager/releases/download/v4.0.0/" + x.Name,
                digest = x.Digest
            })
        }));

    private static string FindRepoRoot()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path != null && !File.Exists(Path.Combine(path.FullName, "DlssNrManager.csproj")))
            path = path.Parent;
        return path?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
