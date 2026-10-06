using System.Net.Http;
using System.Net.Http.Headers;
using System.IO.Compression;
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
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
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

        var packageEntries = await ReadLatestStreamlinePackageEntriesAsync(
            release,
            progress,
            cancellationToken);

        var searchable = dlssPaths
            .Concat(streamlinePaths)
            .Concat(release.Assets.Select(x => x.Name))
            .Concat(packageEntries)
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
        var hasCausticaVulkanHeader = found.Contains(
            "nvsdk_ngx_helpers_dlssnr_vk.h",
            StringComparer.OrdinalIgnoreCase);
        var hasRuntime = found.Any(x =>
            x.Equals("nvngx_dlssnr.dll", StringComparison.OrdinalIgnoreCase) ||
            x.Equals("sl.dlss_nr.dll", StringComparison.OrdinalIgnoreCase));
        var ready = hasCausticaVulkanHeader && hasRuntime;

        var summary = ready
            ? $"Public NVIDIA DLSS Neural Rendering files detected in official sources ({release.Tag})."
            : $"No complete public DLSS Neural Rendering SDK/runtime detected yet ({release.Tag}).";

        var details =
            $"Official sources checked: {DlssRepository}, {StreamlineRepository}. " +
            "Caustica-ready requires nvsdk_ngx_helpers_dlssnr_vk.h plus an official DLSS-NR runtime. " +
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

    private async Task<ReleaseSnapshot> ReadLatestReleaseAsync(
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
                .Select(x => new ReleaseAsset(
                    x.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    x.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : ""))
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .ToList()
            : [];

        return new ReleaseSnapshot(tag, assets);
    }

    private async Task<IReadOnlyList<string>> ReadLatestStreamlinePackageEntriesAsync(
        ReleaseSnapshot release,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var asset = release.Assets
            .Where(x => x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Name.Contains("source", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Name.Contains("arm", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x =>
                x.Name.Contains("x64", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains("win64", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Name.Length)
            .FirstOrDefault();

        if (asset == null || string.IsNullOrWhiteSpace(asset.Url))
            return [];

        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager",
            "nvidia-nr-discovery");
        Directory.CreateDirectory(cacheRoot);
        var safeTag = string.Concat(release.Tag.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var entryCache = Path.Combine(cacheRoot, safeTag + ".entries.txt");

        if (File.Exists(entryCache) &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(entryCache) < TimeSpan.FromHours(12))
        {
            progress?.Report($"Using cached NVIDIA Streamline {release.Tag} package index…");
            return await File.ReadAllLinesAsync(entryCache, cancellationToken);
        }

        progress?.Report($"Inspecting official NVIDIA Streamline {release.Tag} package…");

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "DlssNrManager",
            "nvidia-nr-discovery");
        Directory.CreateDirectory(tempRoot);
        var tempZip = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.zip");

        try
        {
            using var response = await _http.GetAsync(
                asset.Url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                             tempZip,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            using var archive = ZipFile.OpenRead(tempZip);
            var entries = archive.Entries
                .Select(entry => entry.FullName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            await File.WriteAllLinesAsync(entryCache, entries, cancellationToken);
            return entries;
        }
        finally
        {
            try
            {
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }
            catch
            {
                // Discovery is read-only; cleanup failure must not change the result.
            }
        }
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

    private sealed record ReleaseAsset(string Name, string Url);
    private sealed record ReleaseSnapshot(string Tag, IReadOnlyList<ReleaseAsset> Assets);
}
