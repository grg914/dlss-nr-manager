using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

public sealed record ManagerReleaseInfo(
    Version Version,
    string Tag,
    string HtmlUrl,
    string AssetName,
    string AssetUrl,
    string? Sha256);

public sealed class GitHubReleaseService
{
    private const long MaxReleaseAssetBytes = 1024L * 1024 * 1024;

    private readonly HttpClient _http = new();

    public GitHubReleaseService()
    {
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseInfo?> GetLatestAsync(
        bool includePrerelease)
    {
        const int pageSize = 100;
        const int maxPages = 5;

        for (var page = 1; page <= maxPages; page++)
        {
            using var response = await _http.GetAsync(
                $"https://api.github.com/repos/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases?per_page={pageSize}&page={page}",
                HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            await using var stream =
                await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var count = 0;

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                count++;

                if (release.GetProperty("draft").GetBoolean())
                    continue;

                var prerelease =
                    release.GetProperty("prerelease").GetBoolean();
                if (prerelease && !includePrerelease)
                    continue;

                var tag =
                    release.GetProperty("tag_name").GetString()
                    ?? "unknown";
                var name =
                    release.GetProperty("name").GetString()
                    ?? tag;

                var candidates = release.GetProperty("assets")
                    .EnumerateArray()
                    .Select(asset =>
                    {
                        var assetName =
                            asset.GetProperty("name").GetString()
                            ?? string.Empty;
                        var url =
                            asset.GetProperty(
                                "browser_download_url")
                            .GetString();
                        var digest =
                            asset.TryGetProperty(
                                "digest",
                                out var digestElement)
                                ? digestElement.GetString()
                                : null;

                        return new
                        {
                            AssetName = assetName,
                            Url = url,
                            Digest = digest
                        };
                    })
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Url) &&
                        x.AssetName.EndsWith(
                            ".zip",
                            StringComparison.OrdinalIgnoreCase) &&
                        x.AssetName.Contains(
                            "OptiScaler-NR",
                            StringComparison.OrdinalIgnoreCase) &&
                        !x.AssetName.Contains(
                            "rtx40-mfg",
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.AssetName.Length)
                    .ToList();

                var asset = candidates.FirstOrDefault();
                if (asset == null)
                    continue;

                var sha256 =
                    asset.Digest != null &&
                    asset.Digest.StartsWith(
                        "sha256:",
                        StringComparison.OrdinalIgnoreCase)
                        ? asset.Digest["sha256:".Length..]
                        : null;

                return new ReleaseInfo(
                    tag,
                    name,
                    prerelease,
                    asset.Url!,
                    sha256);
            }

            if (count < pageSize)
                break;
        }

        return null;
    }

    public async Task DownloadAsync(
        string url,
        string destination,
        string? expectedSha256 = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(
                "github.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unexpected GitHub release asset URL: {url}");
        }

        var temp = destination + ".download";

        try
        {
            using var response = await _http.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaxReleaseAssetBytes)
            {
                throw new InvalidDataException(
                    "GitHub release asset exceeds the 1 GB safety limit.");
            }

            await using (var source =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken))
            await using (var target = new FileStream(
                temp,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                useAsync: true))
            {
                await CopyWithLimitAsync(
                    source,
                    target,
                    MaxReleaseAssetBytes,
                    cancellationToken);
            }

            if (new FileInfo(temp).Length < 1024)
            {
                throw new InvalidDataException(
                    "Downloaded GitHub release asset is unexpectedly small.");
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actual =
                    await HashService.Sha256Async(temp, cancellationToken);

                if (!actual.Equals(
                        expectedSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Downloaded OptiScaler archive failed SHA-256 verification. Expected {expectedSha256}, got {actual}.");
                }
            }

            File.Move(
                temp,
                destination,
                true);
        }
        catch
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch { }

            throw;
        }
    }

    private static async Task CopyWithLimitAsync(
        Stream input,
        Stream output,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;

        while (true)
        {
            var read = await input.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken);

            if (read == 0)
                break;

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException(
                    $"Download exceeded the {maxBytes / (1024 * 1024)} MB safety limit.");
            }

            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }
    }

    public async Task<ManagerReleaseInfo?> GetLatestManagerReleaseInfoAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(
                "https://api.github.com/repos/grg914/dlss-nr-manager/releases/latest",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            var htmlUrl = doc.RootElement.GetProperty("html_url").GetString();

            if (string.IsNullOrWhiteSpace(tag) ||
                string.IsNullOrWhiteSpace(htmlUrl))
                return null;

            var normalized = tag.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(normalized, out var version))
                return null;

            if (!doc.RootElement.TryGetProperty("assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array)
                return null;

            var candidates = new List<(string Name, string Url, string? Sha256, int Rank)>();

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? ""
                    : "";
                var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString() ?? ""
                    : "";

                if (string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(url))
                    continue;

                var rank =
                    name.Equals("DlssNrManager.exe", StringComparison.OrdinalIgnoreCase) ? 0 :
                    name.Equals("DlssNrManager-win-x64.zip", StringComparison.OrdinalIgnoreCase) ? 1 :
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? 2 :
                    name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 3 :
                    100;

                if (rank >= 100)
                    continue;

                string? sha256 = null;
                if (asset.TryGetProperty("digest", out var digestElement))
                {
                    var digest = digestElement.GetString();
                    if (!string.IsNullOrWhiteSpace(digest) &&
                        digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    {
                        sha256 = digest["sha256:".Length..];
                    }
                }

                candidates.Add((name, url, sha256, rank));
            }

            var selected = candidates
                .OrderBy(x => x.Rank)
                .ThenBy(x => x.Name.Length)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(selected.Name) ||
                string.IsNullOrWhiteSpace(selected.Url))
                return null;

            return new ManagerReleaseInfo(
                version,
                tag,
                htmlUrl,
                selected.Name,
                selected.Url,
                selected.Sha256);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(Version? Version, string? Url)> GetLatestManagerReleaseAsync()
    {
        var release = await GetLatestManagerReleaseInfoAsync();
        return release == null
            ? (null, null)
            : (release.Version, release.HtmlUrl);
    }

}
