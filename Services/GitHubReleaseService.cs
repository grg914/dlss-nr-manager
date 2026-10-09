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
    private const string ManagerReleasesApi =
        "https://api.github.com/repos/grg914/dlss-nr-manager/releases";
    private const string OptiScalerAssetPrefix = "OptiScaler-NR-";
    private const string OptiScalerAssetSuffix = "-vendored-win-x64.zip";

    private readonly HttpClient _http = new();

    public GitHubReleaseService()
    {
        _http.Timeout = TimeSpan.FromMinutes(10);
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
            using var doc = await GetJsonWithRetryAsync(
                $"{ManagerReleasesApi}?per_page={pageSize}&page={page}",
                CancellationToken.None);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var count = 0;

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                count++;

                if (release.TryGetProperty("draft", out var draft) &&
                    draft.GetBoolean())
                    continue;

                var candidate = TryGetManagerOwnedOptiScaler(release);
                if (candidate == null)
                    continue;

                if (candidate.Prerelease && !includePrerelease)
                    continue;

                return candidate;
            }

            if (count < pageSize)
                break;
        }

        return null;
    }

    public async Task<IReadOnlyList<ReleaseInfo>> GetRecentAsync(
        int maxCount = 8,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(maxCount, 1, 20);
        var results = new List<ReleaseInfo>();
        var seenTags = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        const int pageSize = 100;
        const int maxPages = 5;

        for (var page = 1;
             page <= maxPages && results.Count < limit;
             page++)
        {
            using var doc = await GetJsonWithRetryAsync(
                $"{ManagerReleasesApi}?per_page={pageSize}&page={page}",
                cancellationToken);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                break;

            var count = 0;

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                count++;

                if (release.TryGetProperty("draft", out var draft) &&
                    draft.GetBoolean())
                    continue;

                var candidate = TryGetManagerOwnedOptiScaler(release);
                if (candidate == null ||
                    !seenTags.Add(candidate.Tag))
                    continue;

                results.Add(candidate);
                if (results.Count >= limit)
                    break;
            }

            if (count < pageSize)
                break;
        }

        return results;
    }

    internal static ReleaseInfo? TryGetManagerOwnedOptiScaler(
        JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty(
                    "name",
                    out var nameElement)
                ? nameElement.GetString() ?? ""
                : "";

            if (!TryParseOptiScalerTag(name, out var tag))
                continue;

            var url = asset.TryGetProperty(
                    "browser_download_url",
                    out var urlElement)
                ? urlElement.GetString() ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(url))
                continue;

            // Never offer an unverified native DLL archive for installation.
            // The existing manager-owned release must expose a full SHA-256.
            var digest = asset.TryGetProperty("digest", out var digestElement) &&
                         digestElement.ValueKind == JsonValueKind.String
                ? digestElement.GetString()
                : null;
            if (digest is null ||
                digest.Length != "sha256:".Length + 64 ||
                !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                !digest.AsSpan("sha256:".Length).ToString().All(Uri.IsHexDigit))
                continue;

            var sha256 = digest["sha256:".Length..];

            var managerReleasePrerelease =
                release.TryGetProperty(
                    "prerelease",
                    out var prereleaseElement) &&
                prereleaseElement.ValueKind == JsonValueKind.True;

            return new ReleaseInfo(
                tag,
                $"OptiScaler {tag}",
                managerReleasePrerelease,
                url,
                sha256);
        }

        return null;
    }

    internal static bool TryParseOptiScalerTag(
        string assetName,
        out string tag)
    {
        tag = string.Empty;

        if (string.IsNullOrWhiteSpace(assetName) ||
            !assetName.StartsWith(
                OptiScalerAssetPrefix,
                StringComparison.OrdinalIgnoreCase) ||
            !assetName.EndsWith(
                OptiScalerAssetSuffix,
                StringComparison.OrdinalIgnoreCase))
            return false;

        var length =
            assetName.Length -
            OptiScalerAssetPrefix.Length -
            OptiScalerAssetSuffix.Length;

        if (length <= 0)
            return false;

        tag = assetName.Substring(
            OptiScalerAssetPrefix.Length,
            length);

        return !string.IsNullOrWhiteSpace(tag);
    }

    internal static bool IsOptiScalerPrerelease(
        string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        return tag.Contains("-pre", StringComparison.OrdinalIgnoreCase) ||
               tag.Contains("-rc", StringComparison.OrdinalIgnoreCase) ||
               tag.Contains("-alpha", StringComparison.OrdinalIgnoreCase) ||
               tag.Contains("-beta", StringComparison.OrdinalIgnoreCase) ||
               tag.Contains("-dev", StringComparison.OrdinalIgnoreCase);
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
            await NetworkRetry.ExecuteAsync(
                async (attempt, token) =>
                {
                    if (attempt > 1)
                    {
                        try
                        {
                            if (File.Exists(temp))
                                File.Delete(temp);
                        }
                        catch { }
                    }

                    using var response = await _http.GetAsync(
                        uri,
                        HttpCompletionOption.ResponseHeadersRead,
                        token);
                    response.EnsureSuccessStatusCode();

                    if (response.Content.Headers.ContentLength is > MaxReleaseAssetBytes)
                    {
                        throw new InvalidDataException(
                            "GitHub release asset exceeds the 1 GB safety limit.");
                    }

                    using var transfer =
                        DownloadProgressHub.Begin(
                            Path.GetFileName(destination),
                            response.Content.Headers.ContentLength);

                    try
                    {
                        await using (var source =
                            await response.Content.ReadAsStreamAsync(token))
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
                                transfer,
                                token);
                        }

                        transfer.Complete();
                    }
                    catch (Exception ex)
                    {
                        transfer.Fail(ex);
                        throw;
                    }

                    if (new FileInfo(temp).Length < 1024)
                    {
                        throw new InvalidDataException(
                            "Downloaded GitHub release asset is unexpectedly small.");
                    }

                    if (!string.IsNullOrWhiteSpace(expectedSha256))
                    {
                        var actual =
                            await HashService.Sha256Async(temp, token);

                        if (!actual.Equals(
                                expectedSha256,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                $"Downloaded OptiScaler archive failed SHA-256 verification. Expected {expectedSha256}, got {actual}.");
                        }
                    }
                },
                cancellationToken,
                attempts: 3);

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
        DownloadProgressHandle progress,
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

            progress.Report(total);
        }
    }

    public async Task<ManagerReleaseInfo?> GetLatestManagerReleaseInfoAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = await GetJsonWithRetryAsync(
                "https://api.github.com/repos/grg914/dlss-nr-manager/releases/latest",
                cancellationToken);

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
        catch (Exception ex)
        {
            AppLogger.Warn(
                $"Manager release lookup failed after retries: {ex.Message}");
            return null;
        }
    }

    private async Task<JsonDocument> GetJsonWithRetryAsync(
        string url,
        CancellationToken cancellationToken)
    {
        JsonDocument? document = null;

        await NetworkRetry.ExecuteAsync(
            async (_, token) =>
            {
                using var response = await _http.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    token);
                response.EnsureSuccessStatusCode();

                await using var stream =
                    await response.Content.ReadAsStreamAsync(token);

                document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: token);
            },
            cancellationToken,
            attempts: 3);

        return document ??
               throw new InvalidOperationException(
                   "GitHub returned no release metadata.");
    }

    public async Task<(Version? Version, string? Url)> GetLatestManagerReleaseAsync()
    {
        var release = await GetLatestManagerReleaseInfoAsync();
        return release == null
            ? (null, null)
            : (release.Version, release.HtmlUrl);
    }

}
