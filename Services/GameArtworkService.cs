using System.Net.Http;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

public sealed class GameArtworkService
{
    private const int MaxConcurrentLookups = 4;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan NegativeCacheLifetime = TimeSpan.FromHours(12);
    private const int ArtworkCacheVersion = 2;

    private readonly HttpClient _http = new();
    private readonly ConcurrentDictionary<string, ArtworkCacheEntry> _cache;

    private static readonly string AppDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager");

    private static readonly string CachePath = Path.Combine(
        AppDataRoot,
        "artwork-cache.json");

    public static string ArtworkDirectory { get; } = Path.Combine(
        AppDataRoot,
        "artwork");

    public GameArtworkService()
    {
        _http.Timeout = TimeSpan.FromSeconds(8);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", "0.6"));

        _cache = LoadCache();
    }

    public void ClearCache()
    {
        _cache.Clear();

        try
        {
            if (File.Exists(CachePath))
                File.Delete(CachePath);
        }
        catch { }

        try
        {
            if (Directory.Exists(ArtworkDirectory))
                Directory.Delete(ArtworkDirectory, true);
        }
        catch { }
    }

    public async Task<IReadOnlyList<DetectedGame>> ResolveAsync(
        IReadOnlyList<DetectedGame> games,
        CancellationToken cancellationToken = default)
    {
        using var limiter = new SemaphoreSlim(MaxConcurrentLookups);

        var tasks = games.Select(async game =>
        {
            await limiter.WaitAsync(cancellationToken);
            try
            {
                return await ResolveOneAsync(game, cancellationToken);
            }
            finally
            {
                limiter.Release();
            }
        });

        var resolved = await Task.WhenAll(tasks);
        SaveCache();
        return resolved;
    }

    private async Task<DetectedGame> ResolveOneAsync(
        DetectedGame game,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(game.ArtworkUrl))
            return game;

        var local = FindLocalArtwork(game.InstallRoot, game.Platform);
        if (local != null)
            return game with { ArtworkUrl = local };

        var key = NormalizeTitle(game.Name);
        if (string.IsNullOrWhiteSpace(key))
            return game;

        if (_cache.TryGetValue(key, out var cached))
        {
            var lifetime = string.IsNullOrWhiteSpace(cached.Url)
                ? NegativeCacheLifetime
                : CacheLifetime;

            if (cached.Version == ArtworkCacheVersion &&
                DateTimeOffset.UtcNow - cached.CreatedAt < lifetime)
            {
                if (string.IsNullOrWhiteSpace(cached.Url))
                    return game;

                if (File.Exists(cached.Url))
                    return game with { ArtworkUrl = cached.Url };

                var restored = await CacheRemoteArtworkAsync(
                    key,
                    cached.Url,
                    cancellationToken);

                return restored == null
                    ? game
                    : game with { ArtworkUrl = restored };
            }
        }

        var knownAppId = GetKnownSteamAppId(game);
        var url = knownAppId == null
            ? null
            : await GetSteamArtworkByAppIdAsync(knownAppId.Value, cancellationToken);

        if (url == null)
        {
            var lookupName = GetArtworkLookupName(game.Name);
            url = await FindBestSteamArtworkAsync(lookupName, cancellationToken);
        }
        var localUrl = url == null
            ? null
            : await CacheRemoteArtworkAsync(key, url, cancellationToken);

        _cache[key] = new ArtworkCacheEntry(
            localUrl ?? url,
            DateTimeOffset.UtcNow,
            ArtworkCacheVersion);

        return localUrl == null ? game : game with { ArtworkUrl = localUrl };
    }

    private async Task<string?> GetSteamArtworkByAppIdAsync(
        int appId,
        CancellationToken cancellationToken)
    {
        foreach (var url in new[]
        {
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg",
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg"
        })
        {
            if (await UrlExistsAsync(url, cancellationToken))
                return url;
        }

        return null;
    }

    private async Task<string?> FindBestSteamArtworkAsync(
        string gameName,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestUrl =
                "https://store.steampowered.com/api/storesearch/?" +
                $"term={Uri.EscapeDataString(gameName)}&l=english&cc=US";

            using var response = await _http.GetAsync(
                requestUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!json.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return null;

            var wanted = NormalizeTitle(gameName);
            var best = items.EnumerateArray()
                .Select(item =>
                {
                    if (!item.TryGetProperty("name", out var nameElement) ||
                        !item.TryGetProperty("id", out var idElement))
                        return (Item: item, Score: -1);

                    var candidateName = nameElement.GetString();
                    if (string.IsNullOrWhiteSpace(candidateName))
                        return (Item: item, Score: -1);

                    return (Item: item, Score: TitleScore(wanted, NormalizeTitle(candidateName)));
                })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (best.Score < 80)
                return null;

            foreach (var item in new[] { best.Item })
            {
                if (!item.TryGetProperty("name", out var nameElement) ||
                    !item.TryGetProperty("id", out var idElement))
                    continue;

                return await GetSteamArtworkByAppIdAsync(
                    idElement.GetInt32(),
                    cancellationToken);
            }
        }
        catch
        {
            // Artwork is cosmetic. Detection/install must never fail because a store is offline.
        }

        return null;
    }

    private async Task<string?> CacheRemoteArtworkAsync(
        string key,
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(ArtworkDirectory);

            using var response = await _http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentType?.MediaType?.StartsWith(
                    "image/",
                    StringComparison.OrdinalIgnoreCase) != true)
                return null;

            var extension = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".jpg"
            };

            var safeKey = Regex.Replace(key, @"[^a-z0-9]+", "_");
            var destination = Path.Combine(ArtworkDirectory, safeKey + extension);
            var temporary = destination + ".tmp";

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            if (new FileInfo(temporary).Length < 1024)
            {
                File.Delete(temporary);
                return null;
            }

            File.Move(temporary, destination, true);
            return destination;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> UrlExistsAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 0);

            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            return response.IsSuccessStatusCode &&
                   response.Content.Headers.ContentType?.MediaType?.StartsWith(
                       "image/",
                       StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindLocalArtwork(string root, string platform)
    {
        if (!Directory.Exists(root))
            return null;

        var explicitArtwork = platform.Equals("Xbox App", StringComparison.OrdinalIgnoreCase)
            ? FindXboxArtwork(root)
            : null;

        if (explicitArtwork != null)
            return explicitArtwork;

        var preferredNames = new[]
        {
            "cover", "poster", "boxart", "box_art", "keyart", "key_art",
            "library_600x900", "library_600x900_2x", "portrait", "vertical",
            "storelogo", "store_logo"
        };

        var extensions = new HashSet<string>(
            new[] { ".jpg", ".jpeg", ".png", ".bmp" },
            StringComparer.OrdinalIgnoreCase);

        var queue = new Queue<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(string Path, int Score)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0 && visited.Count < 500)
        {
            var (directory, depth) = queue.Dequeue();
            if (!visited.Add(directory))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (!extensions.Contains(Path.GetExtension(file)))
                        continue;

                    var baseName = Path.GetFileNameWithoutExtension(file)
                        .Replace("-", "_")
                        .Replace(" ", "_");

                    var score = preferredNames
                        .Select((value, index) => new { value, index })
                        .Where(x => baseName.Contains(x.value, StringComparison.OrdinalIgnoreCase))
                        .Select(x => 100 - x.index)
                        .DefaultIfEmpty(0)
                        .Max();

                    if (score > 0)
                        candidates.Add((file, score - depth * 5));
                }
            }
            catch { }

            if (depth >= 3)
                continue;

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    var name = Path.GetFileName(child);
                    if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("_CommonRedist", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("__Installer", StringComparison.OrdinalIgnoreCase))
                        continue;

                    queue.Enqueue((child, depth + 1));
                }
            }
            catch { }
        }

        return candidates
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x =>
            {
                try { return new FileInfo(x.Path).Length; }
                catch { return 0L; }
            })
            .Select(x => x.Path)
            .FirstOrDefault();
    }

    private static string? FindXboxArtwork(string contentRoot)
    {
        var config = Path.Combine(contentRoot, "MicrosoftGame.config");
        if (!File.Exists(config))
            return null;

        try
        {
            var xml = new System.Xml.XmlDocument();
            xml.Load(config);

            var shellVisuals = xml.SelectSingleNode("//ShellVisuals");
            if (shellVisuals?.Attributes == null)
                return null;

            foreach (var attributeName in new[]
                     {
                         "StoreLogo", "Square150x150Logo", "Square44x44Logo", "SplashScreenImage"
                     })
            {
                var relative = shellVisuals.Attributes[attributeName]?.Value;
                if (string.IsNullOrWhiteSpace(relative))
                    continue;

                var path = Path.Combine(
                    contentRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(path))
                    return path;
            }
        }
        catch { }

        return null;
    }

    private static int? GetKnownSteamAppId(DetectedGame game)
    {
        var n = NormalizeTitle(game.Name);

        if (n is "bf6" or "battlefield6" or "battlefieldvi" ||
            n.Contains("battlefield6", StringComparison.Ordinal))
            return 2807960;

        var blackOps6 = n is "bo6" or "blackops6" or "callofdutyblackops6" ||
                        n.Contains("blackops6", StringComparison.Ordinal) ||
                        Directory.Exists(Path.Combine(game.InstallRoot, "mp24"));

        if (blackOps6)
            return 4384550;

        if (n is "callofduty" or "cod")
            return 1938090;

        return null;
    }

    private static string GetArtworkLookupName(string value)
    {
        var n = NormalizeTitle(value);

        if (n is "bf6" or "battlefield6" or "battlefieldvi")
            return "Battlefield 6";

        if (n is "bo6" or "blackops6" or "callofdutyblackops6" ||
            n.Contains("blackops6", StringComparison.Ordinal))
            return "Call of Duty Black Ops 6";

        if (n.Contains("battlefield6", StringComparison.Ordinal))
            return "Battlefield 6";

        return value
            .Replace("™", string.Empty)
            .Replace("®", string.Empty)
            .Trim();
    }

    private static int TitleScore(string wanted, string candidate)
    {
        if (wanted == candidate)
            return 100;

        if (candidate.Contains(wanted, StringComparison.Ordinal) ||
            wanted.Contains(candidate, StringComparison.Ordinal))
            return 92;

        var max = Math.Max(wanted.Length, candidate.Length);
        if (max == 0)
            return 0;

        var distance = Levenshtein(wanted, candidate);
        return Math.Max(0, 100 - (distance * 100 / max));
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        var current = new int[b.Length + 1];

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static string NormalizeTitle(string value)
    {
        var normalized = value.ToLowerInvariant();

        normalized = Regex.Replace(
            normalized,
            @"\b(deluxe|ultimate|standard|complete|definitive|gold|goty|game of the year)\s+edition\b",
            string.Empty,
            RegexOptions.CultureInvariant);

        normalized = normalized
            .Replace("™", string.Empty)
            .Replace("®", string.Empty)
            .Replace("©", string.Empty)
            .Replace("&", "and");

        normalized = Regex.Replace(
            normalized,
            @"[^a-z0-9]+",
            string.Empty,
            RegexOptions.CultureInvariant);

        return normalized;
    }

    private static ConcurrentDictionary<string, ArtworkCacheEntry> LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return new();

            var entries = JsonSerializer.Deserialize<Dictionary<string, ArtworkCacheEntry>>(
                File.ReadAllText(CachePath));

            return entries == null
                ? new()
                : new ConcurrentDictionary<string, ArtworkCacheEntry>(
                    entries,
                    StringComparer.Ordinal);
        }
        catch
        {
            return new();
        }
    }

    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);

            var snapshot = _cache.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);

            File.WriteAllText(
                CachePath,
                JsonSerializer.Serialize(
                    snapshot,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private sealed record ArtworkCacheEntry(
        string? Url,
        DateTimeOffset CreatedAt,
        int Version = 1);
}
