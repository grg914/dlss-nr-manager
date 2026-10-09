using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace DlssNrManager.Services;

public enum OfficialUpstreamState
{
    UpToDate,
    NewVersion,
    SourceChanged,
    ReviewRequired,
    Unavailable
}

public sealed record OfficialUpstreamSource(
    string Id,
    string Repository,
    string Strategy,
    string? Branch,
    string LockedRef,
    string? LockedTag,
    string Promotion);

public sealed record OfficialUpstreamResult(
    OfficialUpstreamSource Source,
    OfficialUpstreamState State,
    string? RemoteReference,
    string OfficialUrl)
{
    public bool RequiresReview =>
        State is OfficialUpstreamState.NewVersion or OfficialUpstreamState.SourceChanged;
}

/// <summary>
/// On-demand, read-only discovery from official GitHub repositories.
/// This NEVER downloads binaries or changes the manager-owned runtime.
/// A changed branch SHA means "review", not "a newer compatible version".
/// </summary>
public sealed class OfficialUpstreamUpdateService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private readonly HttpClient _http;
    private DateTimeOffset _cachedAt;
    private IReadOnlyList<OfficialUpstreamResult>? _cached;

    public OfficialUpstreamUpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    internal static IReadOnlyList<OfficialUpstreamSource> GetCatalog()
    {
        using var upstreams = ReadResource("DlssNrManager.OfficialUpstreams");
        using var locked = ReadResource("DlssNrManager.LockedDependencies");
        var pins = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in new[] { "sources", "local_only" })
        {
            if (!locked.RootElement.TryGetProperty(group, out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var entry in entries.EnumerateArray())
            {
                var id = Read(entry, "id");
                if (!string.IsNullOrEmpty(id))
                    pins[id] = entry;
            }
        }

        var catalog = new List<OfficialUpstreamSource>();
        foreach (var entry in upstreams.RootElement.GetProperty("sources").EnumerateArray())
        {
            if (Read(entry, "provider") != "github" ||
                !entry.TryGetProperty("enabled", out var enabled) ||
                enabled.ValueKind != JsonValueKind.True)
                continue;

            var id = Read(entry, "id");
            var repository = Read(entry, "repository");
            var strategy = Read(entry, "strategy");
            if (!IsOfficialRepository(repository) || !pins.TryGetValue(id, out var pin))
                continue;

            var tagField = Read(entry, "lock_tag_field");
            var tag = !string.IsNullOrEmpty(tagField) ? Read(pin, tagField) : "";
            catalog.Add(new OfficialUpstreamSource(
                id,
                repository,
                strategy,
                Read(entry, "branch"),
                Read(pin, "ref"),
                string.IsNullOrWhiteSpace(tag) ? null : tag,
                Read(entry, "promotion")));
        }
        return catalog;
    }

    public async Task<IReadOnlyList<OfficialUpstreamResult>> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheDuration)
            return _cached;

        using var limit = new SemaphoreSlim(4);
        var queries = GetCatalog().Select(async source =>
        {
            await limit.WaitAsync(cancellationToken);
            try { return await CheckOneAsync(source, cancellationToken); }
            finally { limit.Release(); }
        });

        var results = await Task.WhenAll(queries);
        cancellationToken.ThrowIfCancellationRequested();
        _cached = results.OrderBy(x => x.Source.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        _cachedAt = DateTimeOffset.UtcNow;
        return _cached;
    }

    private async Task<OfficialUpstreamResult> CheckOneAsync(
        OfficialUpstreamSource source,
        CancellationToken cancellationToken)
    {
        // Never turn an upstream response into an executable download URL.
        var home = "https://github.com/" + source.Repository;
        var result = new OfficialUpstreamResult(
            source, OfficialUpstreamState.Unavailable, null, home);
        var api = "https://api.github.com/repos/" + source.Repository;
        try
        {
            if (source.Strategy is "default-branch-head" or "branch-head")
            {
                var suffix = "/commits?per_page=1";
                if (!string.IsNullOrWhiteSpace(source.Branch))
                    suffix += "&sha=" + Uri.EscapeDataString(source.Branch);
                using var doc = await FetchAsync(api + suffix, cancellationToken);
                if (doc.RootElement.ValueKind != JsonValueKind.Array ||
                    doc.RootElement.GetArrayLength() == 0)
                    return result;
                var sha = Read(doc.RootElement[0], "sha");
                return result with
                {
                    RemoteReference = sha,
                    State = CompareRevision(source.LockedRef, sha)
                };
            }

            if (source.Strategy == "latest-release")
            {
                using var doc = await FetchAsync(api + "/releases/latest", cancellationToken);
                var tag = Read(doc.RootElement, "tag_name");
                return result with
                {
                    OfficialUrl = home + "/releases",
                    RemoteReference = tag,
                    State = CompareVersion(source.LockedTag, tag)
                };
            }

            if (source.Strategy == "latest-tag")
            {
                using var doc = await FetchAsync(api + "/tags?per_page=100", cancellationToken);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return result;
                var latest = doc.RootElement.EnumerateArray()
                    .Select(x => Read(x, "name"))
                    .Where(x => TryVersion(x, out _))
                    .OrderByDescending(x => { TryVersion(x, out var parsed); return parsed; })
                    .FirstOrDefault();
                return result with
                {
                    OfficialUrl = home + "/tags",
                    RemoteReference = latest,
                    State = CompareVersion(source.LockedTag, latest)
                };
            }

            // E.g. "latest-compatible-release" requires Minecraft-version
            // filtering; a plain newest GitHub tag must not trigger an offer.
            return result with { State = OfficialUpstreamState.ReviewRequired };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return result;
        }
        catch (HttpRequestException) { return result; }
        catch (JsonException) { return result; }
        catch (InvalidOperationException) { return result; }
    }

    private async Task<JsonDocument> FetchAsync(string url, CancellationToken token)
    {
        using var response = await _http.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token);
    }

    internal static OfficialUpstreamState CompareRevision(string? locked, string? remote)
    {
        if (string.IsNullOrWhiteSpace(locked) || string.IsNullOrWhiteSpace(remote) ||
            locked.Length != 40 || remote.Length != 40 ||
            !locked.All(Uri.IsHexDigit) || !remote.All(Uri.IsHexDigit))
            return OfficialUpstreamState.ReviewRequired;
        return string.Equals(locked, remote, StringComparison.OrdinalIgnoreCase)
            ? OfficialUpstreamState.UpToDate
            : OfficialUpstreamState.SourceChanged;
    }

    internal static OfficialUpstreamState CompareVersion(string? locked, string? remote)
    {
        if (!TryVersion(locked, out var oldVersion) ||
            !TryVersion(remote, out var newVersion))
            return OfficialUpstreamState.ReviewRequired;

        // Never offer a downgrade just because an upstream tag changed.
        return newVersion > oldVersion ? OfficialUpstreamState.NewVersion :
            newVersion == oldVersion ? OfficialUpstreamState.UpToDate :
            OfficialUpstreamState.ReviewRequired;
    }

    private static bool TryVersion(string? tag, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;
        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V') ||
            value.StartsWith('n') || value.StartsWith('N'))
            value = value[1..];
        return value.All(c => char.IsAsciiDigit(c) || c == '.') &&
               Version.TryParse(value, out version!);
    }

    private static bool IsOfficialRepository(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2 &&
               parts.All(part =>
                   part.Length is > 0 and <= 100 &&
                   part.All(c => char.IsAsciiLetterOrDigit(c) ||
                                 c is '-' or '_' or '.'));
    }

    private static string Read(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static JsonDocument ReadResource(string name)
    {
        using var stream = typeof(OfficialUpstreamUpdateService)
            .Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing bundled upstream catalog: " + name);
        // DEPENDENCIES.lock.json is stored with a UTF-8 BOM by the lock
        // writer. Decode text before parsing; raw UTF-8 JSON readers may
        // reject the BOM as an invalid initial token.
        using var reader = new StreamReader(
            stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return JsonDocument.Parse(reader.ReadToEnd());
    }
}
