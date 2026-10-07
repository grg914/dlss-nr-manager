using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record NvidiaDlssNrAvailability(
    bool PublicSdkReady,
    string StreamlineVersion,
    IReadOnlyList<string> FoundFiles,
    IReadOnlyList<string> MissingFiles,
    string Summary,
    string Details);

public sealed class NvidiaDlssNrDiscoveryService
{
    private const string ManagerLatestReleaseApi =
        "https://api.github.com/repos/grg914/dlss-nr-manager/releases/latest";
    private const string StreamlinePrefix = "streamline-runtime-v";
    private const string StreamlineSuffix = "-win-x64.zip";

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    public NvidiaDlssNrDiscoveryService()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<NvidiaDlssNrAvailability> CheckAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Checking manager-owned NVIDIA Streamline runtime asset…");

        using var release = await GetJsonAsync(ManagerLatestReleaseApi, cancellationToken);
        var managerTag = release.RootElement.TryGetProperty(
                "tag_name",
                out var tagElement)
            ? tagElement.GetString() ?? "latest"
            : "latest";

        JsonElement? streamlineAsset = null;

        if (release.RootElement.TryGetProperty("assets", out var assets) &&
            assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? ""
                    : "";

                if (name.StartsWith(StreamlinePrefix, StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(StreamlineSuffix, StringComparison.OrdinalIgnoreCase))
                    streamlineAsset = asset.Clone();
            }
        }

        var found = new List<string>();
        var missing = new List<string>();

        if (HasSha256Digest(streamlineAsset))
            found.Add("manager-owned Streamline runtime bundle; bootstrap policy requires sl.dlss_nr.dll + nvngx_dlssnr.dll for Neural Rendering");
        else
            missing.Add("streamline-runtime-v*-win-x64.zip");

        var ready = missing.Count == 0;
        var streamlineVersion =
            streamlineAsset is { } streamAsset
                ? GetStreamlineVersion(
                      streamAsset.GetProperty("name").GetString() ?? "")
                  ?? managerTag
                : managerTag;

        var summary = ready
            ? $"Manager-owned NVIDIA Streamline runtime is ready ({streamlineVersion})."
            : $"Manager-owned NVIDIA Streamline runtime bootstrap is incomplete ({managerTag}).";

        var details =
            $"DLSS NR Manager release checked: {managerTag}. " +
            $"Found: {(found.Count == 0 ? "none" : string.Join(", ", found))}. " +
            $"Missing: {(missing.Count == 0 ? "none" : string.Join(", ", missing))}. " +
            "Game runtime packages are consumed only from manager-owned DLSS NR Manager releases. " +
            "video2dlssnr_release.zip remains isolated to the media/video pipeline.";

        return new NvidiaDlssNrAvailability(
            ready,
            streamlineVersion,
            found,
            missing,
            summary,
            details);
    }

    private static bool HasSha256Digest(JsonElement? asset)
    {
        if (asset is not { } value ||
            !value.TryGetProperty("digest", out var digestElement))
            return false;

        var digest = digestElement.GetString();
        return !string.IsNullOrWhiteSpace(digest) &&
               digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
               digest.Length == "sha256:".Length + 64;
    }

    private static string? GetStreamlineVersion(string assetName)
    {
        if (!assetName.StartsWith(StreamlinePrefix, StringComparison.OrdinalIgnoreCase) ||
            !assetName.EndsWith(StreamlineSuffix, StringComparison.OrdinalIgnoreCase))
            return null;

        var length =
            assetName.Length -
            StreamlinePrefix.Length -
            StreamlineSuffix.Length;

        return length <= 0
            ? null
            : assetName.Substring(StreamlinePrefix.Length, length);
    }

    private async Task<JsonDocument> GetJsonAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }
}
