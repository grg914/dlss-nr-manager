using System.Net.Http.Headers;
using System.Text.Json;
using DlssNrManager.Models;
namespace DlssNrManager.Services;
public sealed class GitHubReleaseService
{
    private readonly HttpClient _http = new();
    public GitHubReleaseService()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DlssNrManager", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseInfo?> GetLatestAsync(bool includePrerelease)
    {
        var json = await _http.GetStringAsync("https://api.github.com/repos/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases?per_page=30");
        using var doc = JsonDocument.Parse(json);
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var pre = release.GetProperty("prerelease").GetBoolean();
            if (pre && !includePrerelease) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "unknown";
            var name = release.GetProperty("name").GetString() ?? tag;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var assetName = asset.GetProperty("name").GetString() ?? "";
                if (!assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (assetName.Contains("rtx40-mfg", StringComparison.OrdinalIgnoreCase)) continue;
                var url = asset.GetProperty("browser_download_url").GetString();
                if (!string.IsNullOrWhiteSpace(url)) return new(tag, name, pre, url);
            }
        }
        return null;
    }

    public async Task DownloadAsync(string url, string destination)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(destination);
        await source.CopyToAsync(target);
    }
}