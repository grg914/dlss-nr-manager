using System.Net.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

public sealed class GameArtworkService
{
    private const int MaxConcurrentLookups = 4;
    private const int ArtworkCacheVersion = 23;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan NegativeCacheLifetime = TimeSpan.FromHours(2);

    private readonly HttpClient _http = new();
    private readonly ConcurrentDictionary<string, ArtworkCacheEntry> _cache;
    private IReadOnlyList<SteamCatalogApp>? _steamCatalog;
    private readonly SemaphoreSlim _steamCatalogLock = new(1, 1);

    private static readonly string AppDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager");

    private static readonly string CachePath = Path.Combine(AppDataRoot, "artwork-cache.json");
    private static readonly string SteamCatalogCachePath = Path.Combine(AppDataRoot, "steam-app-list.json");

    public static string ArtworkDirectory { get; } = Path.Combine(AppDataRoot, "artwork");

    public GameArtworkService()
    {
        _http.Timeout = TimeSpan.FromSeconds(25);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DlssNrManager", "0.8"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        _cache = LoadCache();
    }

    public void ClearCache()
    {
        _cache.Clear();
        _steamCatalog = null;
        TryDeleteFile(CachePath);
        TryDeleteFile(SteamCatalogCachePath);

        // WPF may still have the currently displayed image files open. Delete what
        // is safe to delete, but never make a refresh depend on deleting an old file.
        try
        {
            if (!Directory.Exists(ArtworkDirectory))
                return;

            foreach (var file in Directory.EnumerateFiles(ArtworkDirectory, "*", SearchOption.TopDirectoryOnly))
                TryDeleteFile(file);
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
            catch
            {
                return game with { ArtworkUrl = null };
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
        var key = NormalizeTitle(game.Name);
        if (string.IsNullOrWhiteSpace(key))
            key = NormalizeTitle(Path.GetFileName(game.InstallRoot));

        if (!string.IsNullOrWhiteSpace(game.ArtworkUrl))
        {
            if (File.Exists(game.ArtworkUrl))
                return game;

            if (Uri.TryCreate(game.ArtworkUrl, UriKind.Absolute, out var supplied) &&
                supplied.Scheme is "http" or "https")
            {
                var downloaded = await CacheRemoteArtworkAsync(key, game.ArtworkUrl, cancellationToken);
                if (downloaded != null)
                    return game with { ArtworkUrl = downloaded };
            }
        }

        var local = FindLocalArtwork(game.InstallRoot, game.Platform);
        if (local != null)
            return game with { ArtworkUrl = local };

        if (_cache.TryGetValue(key, out var cached))
        {
            var lifetime = string.IsNullOrWhiteSpace(cached.Url)
                ? NegativeCacheLifetime
                : CacheLifetime;

            if (cached.Version == ArtworkCacheVersion &&
                DateTimeOffset.UtcNow - cached.CreatedAt < lifetime)
            {
                if (!string.IsNullOrWhiteSpace(cached.Url) && File.Exists(cached.Url))
                    return game with { ArtworkUrl = cached.Url };

                if (string.IsNullOrWhiteSpace(cached.Url))
                    return game;
            }
        }

        var candidates = GetArtworkSearchCandidates(game);
        var appId = TryGetSteamAppIdFromInstallRoot(game.InstallRoot) ?? GetKnownSteamAppId(game);
        string? remote = null;

        if (appId != null)
            remote = await GetSteamArtworkByAppIdAsync(appId.Value, cancellationToken);

        if (remote == null)
        {
            foreach (var candidate in candidates)
            {
                remote = await FindBestSteamArtworkAsync(candidate, cancellationToken);
                if (remote != null)
                    break;
            }
        }

        if (remote == null)
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    appId = await FindSteamAppIdFromCatalogAsync(candidate, cancellationToken);
                    if (appId != null)
                        remote = await GetSteamArtworkByAppIdAsync(appId.Value, cancellationToken);
                }
                catch { }

                if (remote != null)
                    break;
            }
        }

        if (remote == null &&
            game.Platform.Equals("Epic", StringComparison.OrdinalIgnoreCase))
        {
            remote = await FindEpicStoreArtworkAsync(candidates, cancellationToken);
        }

        var localUrl = remote == null
            ? null
            : await CacheRemoteArtworkAsync(key, remote, cancellationToken);

        _cache[key] = new ArtworkCacheEntry(localUrl, DateTimeOffset.UtcNow, ArtworkCacheVersion);

        return localUrl == null
            ? game with { ArtworkUrl = null }
            : game with { ArtworkUrl = localUrl };
    }

    private async Task<string?> GetSteamArtworkByAppIdAsync(
        int appId,
        CancellationToken cancellationToken)
    {
        // Steam changed a number of library assets from the legacy predictable
        // /library_600x900.jpg layout to content-hashed paths. Battlefield 6 is
        // one of those titles. Keep verified hashed library assets ahead of the
        // generic fallbacks so EA/Steam installs resolve a portrait cover reliably.
        var knownAsset = appId switch
        {
            2807960 =>
                "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/2807960/289b1c193f9730a0d4ea4dbf912219e46cd1a8a3/library_capsule_2x.jpg",
            _ => null
        };

        if (knownAsset != null &&
            await UrlExistsAsync(knownAsset, cancellationToken))
            return knownAsset;

        foreach (var url in new[]
        {
            $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg",
            $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg",
            $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg",
            $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg",
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg",
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
            $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg",
            $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg",
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg"
        })
        {
            if (await UrlExistsAsync(url, cancellationToken))
                return url;
        }

        try
        {
            var apiUrl = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english&cc=US";
            using var response = await _http.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var key = appId.ToString(CultureInfo.InvariantCulture);

            if (json.RootElement.TryGetProperty(key, out var app) &&
                app.TryGetProperty("success", out var success) &&
                success.ValueKind == JsonValueKind.True &&
                app.TryGetProperty("data", out var data) &&
                data.TryGetProperty("header_image", out var header))
            {
                var headerUrl = header.GetString();
                if (!string.IsNullOrWhiteSpace(headerUrl))
                    return headerUrl;
            }
        }
        catch { }

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

            foreach (var match in items.EnumerateArray()
                         .Select(item =>
                         {
                             if (!item.TryGetProperty("name", out var name) ||
                                 !item.TryGetProperty("id", out _))
                                 return (Item: item, Score: -1);

                             var candidate = name.GetString();
                             return string.IsNullOrWhiteSpace(candidate)
                                 ? (Item: item, Score: -1)
                                 : (Item: item, Score: TitleScore(wanted, NormalizeTitle(candidate)));
                         })
                         .OrderByDescending(x => x.Score)
                         .Where(x => x.Score >= 78)
                         .Take(3))
            {
                if (!match.Item.TryGetProperty("id", out var id))
                    continue;

                var url = await GetSteamArtworkByAppIdAsync(id.GetInt32(), cancellationToken);
                if (url != null)
                    return url;
            }
        }
        catch { }

        return null;
    }

    private async Task<int?> FindSteamAppIdFromCatalogAsync(
        string gameName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameName))
            return null;

        var catalog = await GetSteamCatalogAsync(cancellationToken);
        var wanted = NormalizeTitle(gameName);
        if (string.IsNullOrWhiteSpace(wanted))
            return null;

        var exact = catalog.FirstOrDefault(x => x.NormalizedName == wanted);
        if (exact != null)
            return exact.AppId;

        SteamCatalogApp? best = null;
        var score = 0;

        foreach (var app in catalog)
        {
            var current = TitleScore(wanted, app.NormalizedName);
            if (current > score)
            {
                score = current;
                best = app;
            }
        }

        return score >= 90 ? best?.AppId : null;
    }

    private async Task<IReadOnlyList<SteamCatalogApp>> GetSteamCatalogAsync(
        CancellationToken cancellationToken)
    {
        if (_steamCatalog != null)
            return _steamCatalog;

        await _steamCatalogLock.WaitAsync(cancellationToken);
        try
        {
            if (_steamCatalog != null)
                return _steamCatalog;

            string jsonText;
            var fresh =
                File.Exists(SteamCatalogCachePath) &&
                DateTimeOffset.UtcNow -
                new DateTimeOffset(File.GetLastWriteTimeUtc(SteamCatalogCachePath), TimeSpan.Zero) <
                TimeSpan.FromDays(1);

            if (fresh)
            {
                jsonText = await File.ReadAllTextAsync(SteamCatalogCachePath, cancellationToken);
            }
            else
            {
                using var response = await _http.GetAsync(
                    "https://api.steampowered.com/ISteamApps/GetAppList/v2/",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return Array.Empty<SteamCatalogApp>();

                jsonText = await response.Content.ReadAsStringAsync(cancellationToken);
                Directory.CreateDirectory(AppDataRoot);
                await File.WriteAllTextAsync(SteamCatalogCachePath, jsonText, cancellationToken);
            }

            using var json = JsonDocument.Parse(jsonText);
            var apps = json.RootElement.GetProperty("applist").GetProperty("apps");
            var result = new List<SteamCatalogApp>();

            foreach (var app in apps.EnumerateArray())
            {
                if (!app.TryGetProperty("appid", out var id) ||
                    !app.TryGetProperty("name", out var nameElement))
                    continue;

                var name = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    result.Add(new SteamCatalogApp(id.GetInt32(), NormalizeTitle(name)));
            }

            _steamCatalog = result;
            return result;
        }
        catch
        {
            return Array.Empty<SteamCatalogApp>();
        }
        finally
        {
            _steamCatalogLock.Release();
        }
    }

    private async Task<string?> FindEpicStoreArtworkAsync(
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates)
        {
            var slug = ToStoreSlug(candidate);

            if (!string.IsNullOrWhiteSpace(slug))
            {
                var direct = await GetOpenGraphImageAsync(
                    $"https://store.epicgames.com/en-US/p/{slug}",
                    cancellationToken);

                if (direct != null)
                    return direct;
            }

            try
            {
                var browse =
                    "https://store.epicgames.com/en-US/browse?" +
                    $"q={Uri.EscapeDataString(candidate)}&sortBy=relevancy&sortDir=DESC&count=40";

                using var response = await _http.GetAsync(
                    browse,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    continue;

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var match = Regex.Match(
                    html,
                    @"href\s*=\s*[""'](?<path>/[^""']*/p/[^""'?#]+|/p/[^""'?#]+)[""']",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (!match.Success)
                    continue;

                var path = WebUtility.HtmlDecode(match.Groups["path"].Value);
                var absolute = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? path
                    : "https://store.epicgames.com" + path;

                var image = await GetOpenGraphImageAsync(absolute, cancellationToken);
                if (image != null)
                    return image;
            }
            catch { }
        }

        return null;
    }

    private async Task<string?> GetOpenGraphImageAsync(
        string pageUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(
                pageUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            foreach (var pattern in new[]
            {
                @"<meta[^>]+property\s*=\s*[""']og:image[""'][^>]+content\s*=\s*[""'](?<url>[^""']+)[""']",
                @"<meta[^>]+content\s*=\s*[""'](?<url>[^""']+)[""'][^>]+property\s*=\s*[""']og:image[""']"
            })
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success)
                    continue;

                var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
                if (Uri.TryCreate(url, UriKind.Absolute, out _))
                    return url;
            }
        }
        catch { }

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

            if (!response.IsSuccessStatusCode)
                return null;

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            var extension = mediaType.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".jpg"
            };

            var safeKey = Regex.Replace(key, @"[^a-z0-9]+", "_");
            if (string.IsNullOrWhiteSpace(safeKey))
                safeKey = Guid.NewGuid().ToString("N");

            // Use a fresh file name for every successful refresh. WPF's image
            // decoder can keep the previous file handle alive even after the UI is
            // rebound, so overwriting a stable path is unreliable on Windows.
            var generation = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
            var destination = Path.Combine(ArtworkDirectory, $"{safeKey}_{generation}{extension}");
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

            if (!File.Exists(temporary) || new FileInfo(temporary).Length < 1024)
            {
                TryDeleteFile(temporary);
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
            using var response = await _http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return false;

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            return string.IsNullOrWhiteSpace(mediaType) ||
                   mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
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

        if (platform.Equals("Xbox App", StringComparison.OrdinalIgnoreCase))
        {
            var xbox = FindXboxArtwork(root);
            if (xbox != null)
                return xbox;
        }

        var preferred = new[]
        {
            "cover", "poster", "boxart", "box_art", "keyart", "key_art",
            "library_600x900", "library_600x900_2x", "portrait", "vertical",
            "storelogo", "store_logo", "capsule", "hero"
        };

        var extensions = new HashSet<string>(
            new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" },
            StringComparer.OrdinalIgnoreCase);

        var queue = new Queue<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(string Path, int Score)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0 && visited.Count < 700)
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

                    var score = preferred
                        .Select((value, index) => new { value, index })
                        .Where(x => baseName.Contains(x.value, StringComparison.OrdinalIgnoreCase))
                        .Select(x => 100 - x.index)
                        .DefaultIfEmpty(0)
                        .Max();

                    if (score > 0)
                    {
                        try
                        {
                            var sizeBonus = new FileInfo(file).Length > 20_000 ? 10 : 0;
                            candidates.Add((file, score - depth * 5 + sizeBonus));
                        }
                        catch
                        {
                            candidates.Add((file, score - depth * 5));
                        }
                    }
                }
            }
            catch { }

            if (depth >= 4)
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
            var shell = xml.SelectSingleNode("//ShellVisuals");

            foreach (var attributeName in new[]
            {
                "StoreLogo", "Square150x150Logo", "Square44x44Logo", "SplashScreenImage"
            })
            {
                var relative = shell?.Attributes?[attributeName]?.Value;
                if (string.IsNullOrWhiteSpace(relative))
                    continue;

                var path = Path.Combine(contentRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                    return path;
            }
        }
        catch { }

        return null;
    }

    private static int? TryGetSteamAppIdFromInstallRoot(string installRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                return null;

            var target = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar);

            foreach (var library in GetSteamLibraryRoots())
            {
                var steamApps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(steamApps))
                    continue;

                foreach (var manifest in Directory.EnumerateFiles(
                    steamApps,
                    "appmanifest_*.acf",
                    SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var text = File.ReadAllText(manifest);
                        var installDir = Regex.Match(
                            text,
                            "\"installdir\"\\s+\"([^\"]+)\"",
                            RegexOptions.IgnoreCase);

                        var id = Regex.Match(
                            text,
                            "\"appid\"\\s+\"(\\d+)\"",
                            RegexOptions.IgnoreCase);

                        if (!installDir.Success || !id.Success)
                            continue;

                        var gameRoot = Path.GetFullPath(
                            Path.Combine(steamApps, "common", installDir.Groups[1].Value))
                            .TrimEnd(Path.DirectorySeparatorChar);

                        if (!target.Equals(gameRoot, StringComparison.OrdinalIgnoreCase) &&
                            !target.StartsWith(gameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                            !gameRoot.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (int.TryParse(id.Groups[1].Value, out var appId))
                            return appId;
                    }
                    catch { }
                }
            }
        }
        catch { }

        return null;
    }

    private static IReadOnlyList<string> GetSteamLibraryRoots()
    {
        var result = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            path = path.Trim().Trim('"').Replace(@"\\", @"\");
            try { path = Path.GetFullPath(path); }
            catch { return; }

            if (Directory.Exists(path) &&
                !result.Contains(path, StringComparer.OrdinalIgnoreCase))
                result.Add(path);
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            Add(key?.GetValue("SteamPath")?.ToString());
        }
        catch { }

        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        foreach (var steamRoot in result.ToArray())
        {
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;

            try
            {
                var text = File.ReadAllText(vdf);
                foreach (Match match in Regex.Matches(
                    text,
                    "\"path\"\\s+\"([^\"]+)\"",
                    RegexOptions.IgnoreCase))
                    Add(match.Groups[1].Value);
            }
            catch { }
        }

        return result;
    }

    private static IReadOnlyList<string> GetArtworkSearchCandidates(DetectedGame game)
    {
        var result = new List<string>();

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            value = CleanupDetectedTitle(value);
            if (!string.IsNullOrWhiteSpace(value) &&
                !result.Contains(value, StringComparer.OrdinalIgnoreCase))
                result.Add(value);
        }

        Add(game.Name);
        Add(GetArtworkLookupName(game.Name));

        try
        {
            Add(new DirectoryInfo(game.InstallRoot).Name);
            var target = Directory.Exists(game.TargetDirectory)
                ? game.TargetDirectory
                : game.InstallRoot;

            var exe = Directory.EnumerateFiles(target, "*.exe", SearchOption.TopDirectoryOnly)
                .OrderByDescending(path =>
                {
                    try { return new FileInfo(path).Length; }
                    catch { return 0L; }
                })
                .FirstOrDefault();

            if (exe != null)
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                Add(info.ProductName);
                Add(info.FileDescription);
                Add(Path.GetFileNameWithoutExtension(exe));
            }
        }
        catch { }

        return result;
    }

    private static int? GetKnownSteamAppId(DetectedGame game)
    {
        var n = NormalizeTitle(game.Name);
        var root = game.InstallRoot.ToLowerInvariant();

        if (n is "bf6" or "battlefield6" or "battlefieldvi" ||
            n.Contains("battlefield6", StringComparison.Ordinal) ||
            root.Contains("battlefield 6") ||
            root.Contains("battlefield6"))
            return 2807960;

        var blackOps6 =
            n is "bo6" or "blackops6" or "callofdutyblackops6" ||
            n.Contains("blackops6", StringComparison.Ordinal) ||
            root.Contains("black ops 6") ||
            root.Contains("blackops6") ||
            root.Contains("bo6") ||
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

        return value.Replace("™", string.Empty).Replace("®", string.Empty).Trim();
    }

    private static string CleanupDetectedTitle(string value)
    {
        value = Path.GetFileNameWithoutExtension(value);
        value = Regex.Replace(
            value,
            @"(?i)\b(win64|win32|shipping|x64|x86|dx11|dx12|vulkan|launcher|client|binaries)\b",
            " ");

        return Regex.Replace(
            value.Replace("_", " ").Replace("-", " "),
            @"\s+",
            " ").Trim();
    }

    private static string ToStoreSlug(string value)
    {
        value = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var c in value)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        return builder.ToString().Trim('-');
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

        return Math.Max(0, 100 - Levenshtein(wanted, candidate) * 100 / max);
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

        return Regex.Replace(
            normalized,
            @"[^a-z0-9]+",
            string.Empty,
            RegexOptions.CultureInvariant);
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
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    private sealed record ArtworkCacheEntry(
        string? Url,
        DateTimeOffset CreatedAt,
        int Version = 1);

    private sealed record SteamCatalogApp(int AppId, string NormalizedName);
}
