using System.Text.Json;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

/// <summary>
/// Explicit V4 trial of the original stable-manager OptiScaler asset.
/// Never infer equivalence between same-tag archives with different hashes.
/// </summary>
internal static class OptiScalerLegacyReleasePolicy
{
    internal const string Tag = "v0.7.7-pre0";
    internal const long ArchiveSize = 131421020;
    internal const string ArchiveSha256 =
        "14aa35affd569579709b2705cfa3c2624971a3d9e8e238fe71139954e23e734b";
    internal const string ArchiveName =
        "OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip";
    internal const string ArchiveUrl =
        "https://github.com/grg914/dlss-nr-manager/releases/download/v3.2.0/" +
        ArchiveName;

    private static readonly Lazy<bool> VerifiedReceipt = new(() =>
    {
        using var stream = typeof(OptiScalerLegacyReleasePolicy).Assembly
            .GetManifestResourceStream("DlssNrManager.OptiScalerLegacyCandidate")
            ?? throw new InvalidDataException("Reviewed original OptiScaler receipt was not embedded.");
        using var document = JsonDocument.Parse(stream);
        var receipt = document.RootElement;
        if (receipt.ValueKind != JsonValueKind.Object ||
            !MatchesLong(receipt, "schema", 1) ||
            !MatchesText(receipt, "usage", "trial_requires_explicit_user_confirmation") ||
            !MatchesText(receipt, "source_release", "v3.2.0") ||
            !MatchesLong(receipt, "source_release_id", 406116106) ||
            !MatchesLong(receipt, "asset_id", 620010799) ||
            !MatchesText(receipt, "asset_name", ArchiveName) ||
            !MatchesText(receipt, "sha256", ArchiveSha256) ||
            !MatchesText(receipt, "url", ArchiveUrl) ||
            !MatchesLong(receipt, "size", ArchiveSize))
            throw new InvalidDataException(
                "Bundled original OptiScaler receipt differs from the reviewed source pin.");
        return true;
    });

    internal static void AssertReviewedReceipt() => _ = VerifiedReceipt.Value;

    internal static bool IsPinnedCandidate(ReleaseInfo release)
    {
        AssertReviewedReceipt();
        return release.ZipUrl == ArchiveUrl &&
            string.Equals(release.ZipSha256, ArchiveSha256,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static ReleaseInfo? TrySelect(JsonElement release)
    {
        AssertReviewedReceipt();
        if (release.ValueKind != JsonValueKind.Object ||
            !MatchesLong(release, "id", 406116106) ||
            !MatchesText(release, "tag_name", "v3.2.0") ||
            !IsExplicitFalse(release, "draft") ||
            !IsExplicitFalse(release, "prerelease") ||
            !release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object ||
                !MatchesLong(asset, "id", 620010799) ||
                !MatchesText(asset, "name", ArchiveName) ||
                !MatchesText(asset, "browser_download_url", ArchiveUrl) ||
                !MatchesText(asset, "digest", "sha256:" + ArchiveSha256) ||
                !MatchesLong(asset, "size", ArchiveSize))
                continue;

            return new ReleaseInfo(
                Tag,
                "OptiScaler v0.7.7-pre0 — original v3.2.0 (SHA-256 verified)",
                false,
                ArchiveUrl,
                ArchiveSha256);
        }

        return null;
    }

    private static bool MatchesText(JsonElement obj, string name, string expected) =>
        obj.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static bool MatchesLong(JsonElement obj, string name, long expected) =>
        obj.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var actual) && actual == expected;

    private static bool IsExplicitFalse(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.False;
}
