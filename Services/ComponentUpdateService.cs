using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed class ComponentUpdateService
{
    private readonly HttpClient _http = new();

    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "component-state.json");

    public ComponentUpdateService()
    {
        _http.Timeout = TimeSpan.FromSeconds(12);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<bool> EnsureMediaToolsLatestAsync(
        MediaService media,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        var local = LoadState();
        if (!forceRefresh &&
            media.IsReady &&
            local is { CheckedAt: var checkedAt } &&
            checkedAt != default &&
            DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromHours(6))
        {
            progress?.Report("Media components were checked recently.");
            return false;
        }

        var remote = await GetRemoteStateAsync(cancellationToken);
        var changed =
            !media.IsReady ||
            !string.Equals(local?.ProcessorTag, remote.ProcessorTag, StringComparison.Ordinal) ||
            local?.FfmpegAssetId != remote.FfmpegAssetId;

        if (changed)
        {
            progress?.Report("Updating GitHub media components…");
            await media.UpdateToolsAsync(progress, cancellationToken);
            progress?.Report("GitHub media components updated.");
        }
        else
        {
            progress?.Report("GitHub components are up to date.");
        }

        SaveState(remote with { CheckedAt = DateTimeOffset.UtcNow });
        return changed;
    }

    public async Task<ComponentState> GetRemoteStateAsync(
        CancellationToken cancellationToken = default)
    {
        var processor = await GetJsonAsync(
            "https://api.github.com/repos/DaniilSokolyuk/video2dlssnr/releases/latest",
            cancellationToken);

        var processorTag = processor.RootElement.GetProperty("tag_name").GetString()
            ?? "unknown";

        var ffmpeg = await GetJsonAsync(
            "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/tags/latest",
            cancellationToken);

        long ffmpegAssetId = 0;
        if (ffmpeg.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.Equals(
                        "ffmpeg-master-latest-win64-gpl.zip",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                ffmpegAssetId = asset.GetProperty("id").GetInt64();
                break;
            }
        }

        return new ComponentState(
            processorTag,
            ffmpegAssetId,
            DateTimeOffset.UtcNow);
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

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
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
                JsonSerializer.Serialize(state, new JsonSerializerOptions
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
    DateTimeOffset CheckedAt = default);
