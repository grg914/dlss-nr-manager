using System.Text.Json;

namespace DlssNrManager.Services;

/// <summary>
/// Only exact manager release assets with a complete GitHub SHA-256 digest
/// can be offered to the executable updater. A missing digest fails closed.
/// </summary>
public static class ManagerUpdateAssetPolicy
{
    private const string ExeAsset = "DlssNrManager.exe";
    private const string ZipAsset = "DlssNrManager-win-x64.zip";

    public static bool IsCanonicalAssetName(string? name) =>
        string.Equals(name, ExeAsset, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, ZipAsset, StringComparison.OrdinalIgnoreCase);

    public static bool IsValidSha256(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    public static string? ReadSha256Digest(JsonElement asset)
    {
        if (!asset.TryGetProperty("digest", out var property) ||
            property.ValueKind != JsonValueKind.String)
            return null;

        var digest = property.GetString();
        const string prefix = "sha256:";
        if (digest == null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var sha256 = digest[prefix.Length..];
        return IsValidSha256(sha256) ? sha256 : null;
    }

    public static ManagerReleaseInfo? SelectRelease(JsonElement release)
    {
        var tag = release.TryGetProperty("tag_name", out var tagValue) &&
                  tagValue.ValueKind == JsonValueKind.String
            ? tagValue.GetString()
            : null;
        var htmlUrl = release.TryGetProperty("html_url", out var htmlValue) &&
                      htmlValue.ValueKind == JsonValueKind.String
            ? htmlValue.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(htmlUrl) ||
            !Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var version) ||
            !release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
            return null;

        var candidates = new List<(string Name, string Url, string Sha256, int Rank)>();

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object)
                continue;

            var name = asset.TryGetProperty("name", out var nameValue) &&
                       nameValue.ValueKind == JsonValueKind.String
                ? nameValue.GetString()
                : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlValue) &&
                      urlValue.ValueKind == JsonValueKind.String
                ? urlValue.GetString()
                : null;
            var sha256 = ReadSha256Digest(asset);

            if (!IsCanonicalAssetName(name) || string.IsNullOrWhiteSpace(url) ||
                !IsValidSha256(sha256))
                continue;

            candidates.Add((
                name!,
                url!,
                sha256!,
                name!.Equals(ExeAsset, StringComparison.OrdinalIgnoreCase) ? 0 : 1));
        }

        var selected = candidates.OrderBy(x => x.Rank).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(selected.Name))
            return null;

        return new ManagerReleaseInfo(
            version, tag, htmlUrl, selected.Name, selected.Url, selected.Sha256);
    }
}
