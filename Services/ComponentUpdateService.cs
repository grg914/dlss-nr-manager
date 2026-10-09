using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record ManagerComponentDescriptor(
    string Id,
    string Name,
    string Version,
    string Asset,
    string Sha256,
    string? SourceId,
    string? SourceRef,
    string Channel);

public enum MediaUpdateAvailability
{
    NotInstalled,
    UnknownLocalVersion,
    UnknownRemoteVersion,
    UpToDate,
    UpdateAvailable
}

public sealed class ComponentUpdateService
{
    private const string ManagerLatestReleaseApi =
        "https://api.github.com/repos/grg914/dlss-nr-manager/releases/latest";
    private const string ManagerProcessorAsset = "video2dlssnr_release.zip";
    private const string ManagerFfmpegAsset = "ffmpeg-dlssnr-win-x64.zip";
    private const string ManagerManifestAsset = "components-manifest.json";

    private readonly HttpClient _http = new();

    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "component-state.json");

    public ComponentUpdateService()
    {
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <summary>
    /// Non-mutating, explicitly requested Download Center update check.
    /// An install lacking a recorded manager-owned SHA-256 fingerprint must
    /// not be called outdated by comparing arbitrary release names.
    /// </summary>
    public async Task<MediaUpdateAvailability> CheckMediaUpdateAsync(
        MediaService media,
        CancellationToken cancellationToken = default)
    {
        if (!media.IsReady)
            return MediaUpdateAvailability.NotInstalled;

        var local = LoadState();
        if (string.IsNullOrWhiteSpace(local?.MediaFingerprint) ||
            string.IsNullOrWhiteSpace(local?.ManagerReleaseTag))
            return MediaUpdateAvailability.UnknownLocalVersion;

        var remote = await GetRemoteStateAsync(cancellationToken);
        return EvaluateMediaUpdate(local, remote, installed: true);
    }

    public static MediaUpdateAvailability EvaluateMediaUpdate(
        ComponentState? local,
        ComponentState remote,
        bool installed)
    {
        ArgumentNullException.ThrowIfNull(remote);

        if (!installed)
            return MediaUpdateAvailability.NotInstalled;
        if (string.IsNullOrWhiteSpace(local?.MediaFingerprint) ||
            !TryParseStableManagerVersion(local?.ManagerReleaseTag, out var localVersion))
            return MediaUpdateAvailability.UnknownLocalVersion;
        if (string.IsNullOrWhiteSpace(remote.MediaFingerprint) ||
            !TryParseStableManagerVersion(remote.ManagerReleaseTag, out var remoteVersion))
            return MediaUpdateAvailability.UnknownRemoteVersion;

        // Different bytes do not establish a newer version. In particular,
        // never offer to downgrade a local prerelease/newer stable install.
        if (remoteVersion <= localVersion)
            return MediaUpdateAvailability.UpToDate;

        return string.Equals(
            local?.MediaFingerprint,
            remote.MediaFingerprint,
            StringComparison.OrdinalIgnoreCase)
                ? MediaUpdateAvailability.UpToDate
                : MediaUpdateAvailability.UpdateAvailable;
    }

    /// <summary>
    /// A confirmed Download Center update must be revalidated against the
    /// latest remote manifest at execution time, not just when the UI offered it.
    /// </summary>
    public static bool IsConfirmedMediaUpgrade(
        ComponentState? local,
        ComponentState remote,
        bool installed) =>
        EvaluateMediaUpdate(local, remote, installed) ==
        MediaUpdateAvailability.UpdateAvailable;

    private static bool TryParseStableManagerVersion(string? tag, out Version parsed)
    {
        parsed = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var value = tag.Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            value = value[1..];

        // The manager's /releases/latest endpoint is stable-only. Do not
        // normalize a prerelease or compare opaque identifiers as versions.
        if (!value.All(c => char.IsAsciiDigit(c) || c == '.') ||
            !Version.TryParse(value, out var candidate) ||
            candidate is null)
            return false;

        // Missing System.Version fields compare as -1. Treat v3.2,
        // v3.2.0 and v3.2.0.0 as the same stable release tag.
        parsed = new Version(
            candidate.Major,
            candidate.Minor,
            Math.Max(0, candidate.Build),
            Math.Max(0, candidate.Revision));
        return true;
    }

    public async Task<bool> EnsureMediaToolsLatestAsync(
        MediaService media,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false,
        bool requireInstalledForUpdate = false)
    {
        var local = LoadState();
        if (!forceRefresh &&
            media.IsReady &&
            local is { CheckedAt: var checkedAt } &&
            checkedAt != default &&
            DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromHours(6))
        {
            progress?.Report("Manager-owned components were checked recently.");
            return false;
        }

        var remote = await GetRemoteStateAsync(cancellationToken);

        // Both entry points (automatic startup and user-confirmed Download
        // Center updates) must reject older or unverified remote releases.
        // A changed fingerprint alone never establishes a newer version.
        if (media.IsReady)
        {
            var availability = EvaluateMediaUpdate(local, remote, installed: true);
            if (availability == MediaUpdateAvailability.UpToDate)
            {
                progress?.Report("Manager-owned media components are already up to date.");
                return false; // Keep the locally verified receipt; never save older remote state.
            }

            if (availability != MediaUpdateAvailability.UpdateAvailable)
            {
                if (!forceRefresh)
                {
                    progress?.Report(
                        "Automatic media update skipped: release or installation receipt is not verified.");
                    return false; // Auto-check cannot replace a working, unverified install.
                }

                throw new InvalidOperationException(
                    "The confirmed media update is no longer a validated upgrade. " +
                    "Refresh Downloads to review the current release.");
            }
        }
        else if (requireInstalledForUpdate)
        {
            // Only the Download Center's confirmed Update action requires
            // the previously installed runtime to still exist. The general
            // Check Updates command may bootstrap missing media tools.
            throw new InvalidOperationException(
                "The media engine is no longer installed. Use Install or Repair instead of Update.");
        }

        var hasManifestFingerprint =
            !string.IsNullOrWhiteSpace(remote.MediaFingerprint);

        var changed =
            !media.IsReady ||
            (hasManifestFingerprint
                ? !string.Equals(
                    local?.MediaFingerprint,
                    remote.MediaFingerprint,
                    StringComparison.OrdinalIgnoreCase)
                : !string.Equals(
                      local?.ProcessorTag,
                      remote.ProcessorTag,
                      StringComparison.Ordinal) ||
                  local?.FfmpegAssetId != remote.FfmpegAssetId);

        if (changed)
        {
            progress?.Report("Updating manager-owned media components…");
            await media.UpdateToolsAsync(progress, cancellationToken);
            progress?.Report("Manager-owned media components updated.");
        }
        else
        {
            progress?.Report("Manager-owned components are up to date.");
        }

        SaveState(remote with { CheckedAt = DateTimeOffset.UtcNow });
        return changed;
    }

    public async Task<IReadOnlyList<ManagerComponentDescriptor>>
        GetAvailableComponentsAsync(
            CancellationToken cancellationToken = default)
    {
        using var manager = await GetJsonAsync(
            ManagerLatestReleaseApi,
            cancellationToken);

        return await TryReadManifestAsync(
                   manager.RootElement,
                   cancellationToken)
               ?? [];
    }

    public async Task<ComponentState> GetRemoteStateAsync(
        CancellationToken cancellationToken = default)
    {
        using var manager = await GetJsonAsync(
            ManagerLatestReleaseApi,
            cancellationToken);

        long processorAssetId = 0;
        long ffmpegAssetId = 0;

        if (manager.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.Equals(
                        ManagerProcessorAsset,
                        StringComparison.OrdinalIgnoreCase))
                {
                    processorAssetId = asset.GetProperty("id").GetInt64();
                }
                else if (name.Equals(
                             ManagerFfmpegAsset,
                             StringComparison.OrdinalIgnoreCase))
                {
                    ffmpegAssetId = asset.GetProperty("id").GetInt64();
                }
            }
        }

        if (processorAssetId == 0)
        {
            throw new InvalidOperationException(
                $"Latest DLSS NR Manager release has no {ManagerProcessorAsset} asset. " +
                "Bootstrap the validated video runtime before checking media updates.");
        }

        if (ffmpegAssetId == 0)
        {
            throw new InvalidOperationException(
                $"Latest DLSS NR Manager release has no {ManagerFfmpegAsset} asset.");
        }

        var manifest = await TryReadManifestAsync(
            manager.RootElement,
            cancellationToken);

        var mediaFingerprint = BuildMediaFingerprint(manifest);

        var releaseTag = manager.RootElement.TryGetProperty("tag_name", out var tagElement) &&
                         tagElement.ValueKind == JsonValueKind.String
            ? tagElement.GetString()
            : null;

        return new ComponentState(
            $"manager:{processorAssetId}",
            ffmpegAssetId,
            DateTimeOffset.UtcNow,
            mediaFingerprint,
            releaseTag);
    }

    private async Task<IReadOnlyList<ManagerComponentDescriptor>?>
        TryReadManifestAsync(
            JsonElement release,
            CancellationToken cancellationToken)
    {
        if (!release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
            return null;

        string? manifestUrl = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;

            if (!string.Equals(
                    name,
                    ManagerManifestAsset,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            manifestUrl = asset.TryGetProperty(
                    "browser_download_url",
                    out var urlElement)
                ? urlElement.GetString()
                : null;

            break;
        }

        if (string.IsNullOrWhiteSpace(manifestUrl))
            return null;

        if (!Uri.TryCreate(
                manifestUrl,
                UriKind.Absolute,
                out var uri) ||
            !uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(
                "github.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unexpected manager component manifest URL: {manifestUrl}");
        }

        using var manifest = await GetJsonAsync(
            uri.AbsoluteUri,
            cancellationToken);

        if (!manifest.RootElement.TryGetProperty(
                "schema",
                out var schemaElement) ||
            schemaElement.GetInt32() != 1)
        {
            throw new InvalidDataException(
                "Unsupported manager component manifest schema.");
        }

        if (!manifest.RootElement.TryGetProperty(
                "components",
                out var components) ||
            components.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Manager component manifest has no components array.");
        }

        var result = new List<ManagerComponentDescriptor>();

        foreach (var component in components.EnumerateArray())
        {
            var id = ReadString(component, "id");
            var name = ReadString(component, "name");
            var version = ReadString(component, "version");
            var asset = ReadString(component, "asset");
            var sha256 = ReadString(component, "sha256");
            var channel = ReadString(component, "channel");

            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(asset) ||
                sha256.Length != 64)
            {
                throw new InvalidDataException(
                    "Manager component manifest contains an invalid component entry.");
            }

            result.Add(
                new ManagerComponentDescriptor(
                    id,
                    name,
                    version,
                    asset,
                    sha256.ToLowerInvariant(),
                    ReadNullableString(component, "source_id"),
                    ReadNullableString(component, "source_ref"),
                    string.IsNullOrWhiteSpace(channel)
                        ? "stable"
                        : channel));
        }

        return result;
    }

    private static string? BuildMediaFingerprint(
        IReadOnlyList<ManagerComponentDescriptor>? components)
    {
        if (components == null)
            return null;

        var processor = components.FirstOrDefault(
            item => item.Id.Equals(
                "video2dlssnr",
                StringComparison.OrdinalIgnoreCase));

        var ffmpeg = components.FirstOrDefault(
            item => item.Id.Equals(
                "ffmpeg",
                StringComparison.OrdinalIgnoreCase));

        if (processor == null || ffmpeg == null)
            return null;

        return $"video2dlssnr:{processor.Sha256}|ffmpeg:{ffmpeg.Sha256}";
    }

    private static string ReadString(
        JsonElement element,
        string property)
        => element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string? ReadNullableString(
        JsonElement element,
        string property)
        => element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private async Task<JsonDocument> GetJsonAsync(
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
                   "Component update metadata could not be loaded.");
    }

    private static ComponentState? LoadState()
    {
        try
        {
            if (!File.Exists(StatePath))
                return null;

            return JsonSerializer.Deserialize<ComponentState>(
                File.ReadAllText(StatePath));
        }
        catch
        {
            return null;
        }
    }

    private static void SaveState(ComponentState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            AtomicFile.WriteAllText(
                StatePath,
                JsonSerializer.Serialize(
                    state,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch { }
    }
}

public sealed record ComponentState(
    string ProcessorTag,
    long FfmpegAssetId,
    DateTimeOffset CheckedAt = default,
    string? MediaFingerprint = null,
    string? ManagerReleaseTag = null);
