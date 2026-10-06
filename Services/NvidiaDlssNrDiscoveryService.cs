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
    private const string StreamlineRepository = "NVIDIA-RTX/Streamline";
    private const string DlssRepository = "NVIDIA/DLSS";

    private static readonly string[] RequiredSignals =
    [
        "nvsdk_ngx_helpers_dlssnr_vk.h",
        "sl_dlss_nr.h",
        "nvngx_dlssnr.dll",
        "sl.dlss_nr.dll"
    ];

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public NvidiaDlssNrDiscoveryService()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", "1.3"));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<NvidiaDlssNrAvailability> CheckAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Checking official NVIDIA DLSS and Streamline repositories…");

        var dlssTreeTask = ReadTreeAsync(DlssRepository, cancellationToken);
        var streamlineTreeTask = ReadTreeAsync(StreamlineRepository, cancellationToken);
        var releaseTask = ReadLatestReleaseAsync(StreamlineRepository, cancellationToken);

        await Task.WhenAll(dlssTreeTask, streamlineTreeTask, releaseTask);

        var dlssPaths = await dlssTreeTask;
        var streamlinePaths = await streamlineTreeTask;
        var release = await releaseTask;

        var searchable = dlssPaths
            .Concat(streamlinePaths)
            .Concat(release.Assets)
            .ToArray();

        var found = RequiredSignals
            .Where(signal => searchable.Any(path =>
                path.Contains(signal, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var missing = RequiredSignals
            .Where(signal => !found.Contains(signal, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // A public usable integration needs both an API/header signal and a runtime DLL signal.
        // A single matching filename is not enough to claim support is deployable.
        var hasHeader = found.Any(x =>
            x.EndsWith(".h", StringComparison.OrdinalIgnoreCase));
        var hasRuntime = found.Any(x =>
            x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        var ready = hasHeader && hasRuntime;

        var summary = ready
            ? $"Public NVIDIA DLSS Neural Rendering files detected in official sources ({release.Tag})."
            : $"No complete public DLSS Neural Rendering SDK/runtime detected yet ({release.Tag}).";

        var details =
            $"Official sources checked: {DlssRepository}, {StreamlineRepository}. " +
            $"Found: {(found.Count == 0 ? "none" : string.Join(", ", found))}. " +
            $"Missing: {(missing.Count == 0 ? "none" : string.Join(", ", missing))}. " +
            "The manager never treats copied, renamed or unofficial DLLs as proof of DLSS-NR availability.";

        return new NvidiaDlssNrAvailability(
            ready,
            release.Tag,
            found,
            missing,
            summary,
            details);
    }

    private async Task<IReadOnlyList<string>> ReadTreeAsync(
        string repository,
        CancellationToken cancellationToken)
    {
        using var repo = await GetJsonAsync(
            $"https://api.github.com/repos/{repository}",
            cancellationToken);

        var branch = repo.RootElement.TryGetProperty("default_branch", out var branchElement)
            ? branchElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(branch))
            branch = "main";

        using var tree = await GetJsonAsync(
            $"https://api.github.com/repos/{repository}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1",
            cancellationToken);

        if (!tree.RootElement.TryGetProperty("tree", out var items))
            return [];

        return items.EnumerateArray()
            .Select(x => x.TryGetProperty("path", out var p) ? p.GetString() : null)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList();
    }

    private async Task<(string Tag, IReadOnlyList<string> Assets)> ReadLatestReleaseAsync(
        string repository,
        CancellationToken cancellationToken)
    {
        using var release = await GetJsonAsync(
            $"https://api.github.com/repos/{repository}/releases/latest",
            cancellationToken);

        var tag = release.RootElement.TryGetProperty("tag_name", out var t)
            ? t.GetString() ?? "latest"
            : "latest";

        var assets = release.RootElement.TryGetProperty("assets", out var assetArray)
            ? assetArray.EnumerateArray()
                .Select(x => x.TryGetProperty("name", out var n) ? n.GetString() : null)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList()
            : [];

        return (tag, assets);
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
