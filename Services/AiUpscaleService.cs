using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DlssNrManager.Services;

public enum AiUpscaleModel
{
    GeneralPhoto,
    GeneralSoft,
    AnimeIllustration,
    AnimeVideo
}

public sealed record AiUpscaleOptions(
    int Scale,
    AiUpscaleModel Model,
    string OutputDirectory,
    bool UseTta = false,
    int TileSize = 0);

public sealed class AiUpscaleService
{
    private const string Repo = "xinntao/Real-ESRGAN-ncnn-vulkan";

    private const long MaxEngineArchiveBytes = 1024L * 1024 * 1024;
    private const long MaxExtractedArchiveBytes = 4L * 1024 * 1024 * 1024;
    private const int MaxArchiveEntries = 100_000;

    private readonly HttpClient _http = new();
    private bool _modelsVerified;

    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "media-engine",
        "realesrgan");

    private string EngineExe =>
        FindFile(RootDirectory, "realesrgan-ncnn-vulkan.exe") ?? "";

    private string ModelsDirectory =>
        Path.Combine(RootDirectory, "models");

    private static readonly ModelAsset[] RequiredModels =
    [
        // Pinned immutable GitHub commits. The current upstream v0.2.0
        // Windows release intentionally does not bundle NCNN model files.
        new(
            "realesrgan-x4plus.param",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesrgan-x4plus.param",
            116029,
            "d14d62ebb815bdd522ed112e67695b3377f86ca0"),
        new(
            "realesrgan-x4plus.bin",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesrgan-x4plus.bin",
            33424520,
            "5cea94783710c25d6fffa9fe9b59999498aec3d4"),
        new(
            "realesrnet-x4plus.param",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesrnet-x4plus.param",
            116029,
            "d14d62ebb815bdd522ed112e67695b3377f86ca0"),
        new(
            "realesrnet-x4plus.bin",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesrnet-x4plus.bin",
            33424520,
            "4f5b87990354b39b744adf25e36f4857065584a6"),
        new(
            "realesrgan-x4plus-anime.param",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesrgan-x4plus-anime.param",
            30290,
            "6c98f9a1932603688683a6f0108cbdfcd6b3e680"),
        new(
            "realesrgan-x4plus-anime.bin",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesrgan-x4plus-anime.bin",
            8943500,
            "95201b7beeefaa2de45bc80f77f879f51d2fc534"),
        new(
            "realesr-animevideov3-x2.param",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesr-animevideov3-x2.param",
            3173,
            "42e774841c35c8bf0ffeb215bb40c61d4868be16"),
        new(
            "realesr-animevideov3-x2.bin",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesr-animevideov3-x2.bin",
            1247368,
            "20691050e279557160fbef5fa3f45fafeeac5402"),
        new(
            "realesr-animevideov3-x3.param",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesr-animevideov3-x3.param",
            3173,
            "bf4718580cc40eac9ff34f730ca64053feaf7bf4"),
        new(
            "realesr-animevideov3-x3.bin",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesr-animevideov3-x3.bin",
            1247368,
            "20691050e279557160fbef5fa3f45fafeeac5402"),
        new(
            "realesr-animevideov3-x4.param",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesr-animevideov3-x4.param",
            3077,
            "5b922cc388374b1152e01fa633bcab80b2448dae"),
        new(
            "realesr-animevideov3-x4.bin",
            "https://github.com/grg914/dlss-nr-manager/releases/latest/download/realesr-animevideov3-x4.bin",
            1247368,
            "20691050e279557160fbef5fa3f45fafeeac5402")
    ];

    private static string MediaRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "media-engine");

    private static string FfmpegExe => Path.Combine(MediaRoot, "tools", "ffmpeg.exe");
    private static string FfprobeExe => Path.Combine(MediaRoot, "tools", "ffprobe.exe");

    public bool IsInstalled =>
        IsUsableFile(EngineExe, 256 * 1024) &&
        HasUsableModels(ModelsDirectory);

    public bool IsReady =>
        IsInstalled &&
        _modelsVerified;

    public AiUpscaleService()
    {
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task SetupAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsReady)
        {
            progress?.Report("AI Upscale engine already installed.");
            return;
        }

        Directory.CreateDirectory(RootDirectory);

        string engineTag = "existing";

        if (!IsUsableFile(EngineExe, 256 * 1024))
        {
            progress?.Report(
                "Checking latest Real-ESRGAN NCNN Vulkan release…");

            using var release = await GetJsonAsync(
                $"https://api.github.com/repos/{Repo}/releases/latest",
                cancellationToken);

            var asset = FindWindowsAsset(release)
                ?? throw new InvalidOperationException(
                    "No Windows ZIP was found in the latest Real-ESRGAN NCNN Vulkan release.");

            engineTag = asset.Tag;

            var zipPath = Path.Combine(
                RootDirectory,
                "realesrgan-windows.zip");
            var extractDir = Path.Combine(
                RootDirectory,
                "_extract");

            TryDeleteDirectory(extractDir);
            Directory.CreateDirectory(extractDir);

            try
            {
                progress?.Report(
                    $"Downloading Real-ESRGAN {asset.Tag}…");

                await DownloadAsync(
                    asset.Url,
                    zipPath,
                    asset.Sha256,
                    cancellationToken);

                progress?.Report(
                    "Extracting AI Upscale engine…");

                ExtractSafe(zipPath, extractDir);

                _ = FindFile(
                        extractDir,
                        "realesrgan-ncnn-vulkan.exe")
                    ?? throw new InvalidOperationException(
                        "Real-ESRGAN executable was not found after extraction.");

                CopyDirectoryContents(
                    extractDir,
                    RootDirectory);
            }
            finally
            {
                TryDeleteDirectory(extractDir);
                TryDeleteFile(zipPath);
            }
        }
        else
        {
            progress?.Report(
                "Real-ESRGAN executable already present • repairing models only…");
        }

        await EnsureRequiredModelsAsync(
            progress,
            cancellationToken);

        _modelsVerified = true;

        if (!IsReady)
        {
            var missing = GetMissingModelFiles();

            throw new InvalidOperationException(
                "Real-ESRGAN setup did not produce a complete engine. " +
                $"Executable: {(File.Exists(EngineExe) ? EngineExe : "not found")}; " +
                $"missing models: {(missing.Count == 0 ? "none" : string.Join(", ", missing))}.");
        }

        progress?.Report(
            $"AI Upscale engine ready • Real-ESRGAN {engineTag} • {RequiredModels.Length} model files verified.");
    }

    public void Reset()
    {
        _modelsVerified = false;
        TryDeleteDirectory(RootDirectory);
    }

    public async Task<string> UpscaleAsync(
        string source,
        AiUpscaleOptions options,
        MediaService media,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(source))
            throw new FileNotFoundException("Input media file was not found.", source);

        if (options.Scale is < 2 or > 4)
            throw new ArgumentOutOfRangeException(
                nameof(options.Scale),
                "AI upscale scale must be 2, 3 or 4.");

        Directory.CreateDirectory(options.OutputDirectory);

        if (!IsReady)
            await SetupAsync(progress, cancellationToken);

        return IsImage(source)
            ? await UpscaleImageAsync(source, options, progress, cancellationToken)
            : await UpscaleVideoAsync(source, options, media, progress, cancellationToken);
    }

    private async Task<string> UpscaleImageAsync(
        string source,
        AiUpscaleOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var output = MakeUniquePath(Path.Combine(
            options.OutputDirectory,
            Path.GetFileNameWithoutExtension(source)
            + $"_upscaled_x{options.Scale}.png"));

        progress?.Report(
            $"AI upscaling image ×{options.Scale} with {DisplayModel(options.Model)}…");

        var result = await RunAsync(
            EngineExe,
            BuildRealEsrganArgs(source, output, options),
            Path.GetDirectoryName(EngineExe)!,
            cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Real-ESRGAN image upscale failed.\n{Tail(result.Error, 3000)}");

        if (!File.Exists(output))
            throw new InvalidOperationException(
                "Real-ESRGAN completed without producing the expected image.");

        progress?.Report($"AI image upscale complete • {output}");
        return output;
    }

    private async Task<string> UpscaleVideoAsync(
        string source,
        AiUpscaleOptions options,
        MediaService media,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(FfmpegExe) || !File.Exists(FfprobeExe))
        {
            progress?.Report("Preparing FFmpeg for video AI upscaling…");
            await media.SetupAsync(progress, cancellationToken);
        }

        if (!File.Exists(FfmpegExe) || !File.Exists(FfprobeExe))
            throw new InvalidOperationException("FFmpeg is required for video AI upscaling.");

        var fps = await ProbeFpsAsync(source, cancellationToken);

        var sessionRoot = Path.Combine(
            Path.GetTempPath(),
            "DlssNrManager",
            "ai-upscale",
            Guid.NewGuid().ToString("N"));

        var inputFrames = Path.Combine(sessionRoot, "input");
        var outputFrames = Path.Combine(sessionRoot, "output");
        Directory.CreateDirectory(inputFrames);
        Directory.CreateDirectory(outputFrames);

        var output = MakeUniquePath(Path.Combine(
            options.OutputDirectory,
            Path.GetFileNameWithoutExtension(source)
            + $"_upscaled_x{options.Scale}.mp4"));

        try
        {
            progress?.Report($"Extracting video frames @ {fps:0.###} FPS…");

            var decode = await RunAsync(
                FfmpegExe,
                new[]
                {
                    "-y",
                    "-hide_banner",
                    "-loglevel", "error",
                    "-i", source,
                    "-map", "0:v:0",
                    // FFmpeg removed/deprecated the legacy global -vsync switch in recent builds.
                    // Use the per-output modern equivalent so every decoded source frame is preserved
                    // without FFmpeg duplicating/dropping frames before Real-ESRGAN processes them.
                    "-fps_mode", "passthrough",
                    Path.Combine(inputFrames, "%08d.png")
                },
                Path.GetDirectoryName(FfmpegExe)!,
                cancellationToken);

            if (decode.ExitCode != 0)
                throw new InvalidOperationException(
                    $"FFmpeg frame extraction failed.\n{Tail(decode.Error, 3000)}");

            var frameCount = Directory.EnumerateFiles(inputFrames, "*.png").Count();
            if (frameCount == 0)
                throw new InvalidOperationException("No video frames were extracted.");

            progress?.Report(
                $"AI upscaling {frameCount:N0} frames ×{options.Scale} with {DisplayModel(options.Model)}…");

            var upscale = await RunAsync(
                EngineExe,
                BuildRealEsrganArgs(inputFrames, outputFrames, options),
                Path.GetDirectoryName(EngineExe)!,
                cancellationToken);

            if (upscale.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Real-ESRGAN video frame upscale failed.\n{Tail(upscale.Error, 3000)}");

            var processedCount = Directory.EnumerateFiles(outputFrames, "*.png").Count();
            if (processedCount != frameCount)
                throw new InvalidOperationException(
                    $"Frame count mismatch: extracted {frameCount:N0}, upscaled {processedCount:N0}.");

            progress?.Report("Encoding upscaled video and audio…");

            var encode = await RunAsync(
                FfmpegExe,
                new[]
                {
                    "-y",
                    "-hide_banner",
                    "-loglevel", "error",
                    "-framerate", fps.ToString("0.########", CultureInfo.InvariantCulture),
                    "-i", Path.Combine(outputFrames, "%08d.png"),
                    "-i", source,
                    "-map", "0:v:0",
                    "-map", "1:a?",
                    "-c:v", "libx264",
                    "-preset", "medium",
                    "-crf", "18",
                    "-pix_fmt", "yuv420p",
                    "-c:a", "aac",
                    "-b:a", "192k",
                    "-shortest",
                    "-movflags", "+faststart",
                    output
                },
                Path.GetDirectoryName(FfmpegExe)!,
                cancellationToken);

            if (encode.ExitCode != 0)
                throw new InvalidOperationException(
                    $"FFmpeg video encode failed.\n{Tail(encode.Error, 3000)}");

            if (!File.Exists(output))
                throw new InvalidOperationException(
                    "Video AI upscale completed without producing the expected output.");

            progress?.Report($"AI video upscale complete • {output}");
            return output;
        }
        finally
        {
            TryDeleteDirectory(sessionRoot);
        }
    }

    private async Task<double> ProbeFpsAsync(
        string source,
        CancellationToken cancellationToken)
    {
        var probe = await RunAsync(
            FfprobeExe,
            new[]
            {
                "-v", "error",
                "-select_streams", "v:0",
                "-show_entries", "stream=avg_frame_rate",
                "-of", "default=noprint_wrappers=1:nokey=1",
                source
            },
            Path.GetDirectoryName(FfprobeExe)!,
            cancellationToken);

        if (probe.ExitCode != 0)
            return 30d;

        var value = probe.Output.Trim();
        if (value.Contains('/'))
        {
            var parts = value.Split('/', 2);
            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) &&
                d > 0)
                return n / d;
        }

        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var fps) && fps > 0
                ? fps
                : 30d;
    }

    private IEnumerable<string> BuildRealEsrganArgs(
        string input,
        string output,
        AiUpscaleOptions options)
    {
        var args = new List<string>
        {
            "-i", input,
            "-o", output,
            "-s", options.Scale.ToString(CultureInfo.InvariantCulture),
            "-n", ModelName(options.Model),
            "-m", ModelsDirectory,
            "-g", "0",
            "-t", Math.Max(0, options.TileSize).ToString(CultureInfo.InvariantCulture),
            "-f", "png"
        };

        if (options.UseTta)
            args.Add("-x");

        return args;
    }

    private static string ModelName(AiUpscaleModel model)
        => model switch
        {
            AiUpscaleModel.GeneralPhoto => "realesrgan-x4plus",
            AiUpscaleModel.GeneralSoft => "realesrnet-x4plus",
            AiUpscaleModel.AnimeIllustration => "realesrgan-x4plus-anime",
            AiUpscaleModel.AnimeVideo => "realesr-animevideov3",
            _ => "realesrgan-x4plus"
        };

    private static string DisplayModel(AiUpscaleModel model)
        => model switch
        {
            AiUpscaleModel.GeneralPhoto => "General / Photo",
            AiUpscaleModel.GeneralSoft => "General / Conservative",
            AiUpscaleModel.AnimeIllustration => "Illustration / Anime",
            AiUpscaleModel.AnimeVideo => "Anime Video / Minecraft",
            _ => model.ToString()
        };

    private static ReleaseAsset? FindWindowsAsset(JsonDocument release)
    {
        var tag = release.RootElement.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString() ?? "unknown"
            : "unknown";

        if (!release.RootElement.TryGetProperty("assets", out var assets))
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? ""
                : "";

            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                !name.Contains("windows", StringComparison.OrdinalIgnoreCase))
                continue;

            var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(url))
                continue;

            string? sha256 = null;
            if (asset.TryGetProperty("digest", out var digestElement))
            {
                var digest = digestElement.GetString();
                if (!string.IsNullOrWhiteSpace(digest) &&
                    digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha256 = digest["sha256:".Length..];
            }

            return new ReleaseAsset(tag, name, url, sha256);
        }

        return null;
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

    private async Task DownloadAsync(
        string url,
        string destination,
        string? expectedSha256,
        CancellationToken cancellationToken)
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
                $"Unexpected Real-ESRGAN release URL: {url}");
        }

        var temp = destination + ".download";

        try
        {
            await NetworkRetry.ExecuteAsync(
                async (attempt, token) =>
                {
                    if (attempt > 1)
                        TryDeleteFile(temp);

                    using var response = await _http.GetAsync(
                        uri,
                        HttpCompletionOption.ResponseHeadersRead,
                        token);

                    response.EnsureSuccessStatusCode();

                    if (response.Content.Headers.ContentLength is > MaxEngineArchiveBytes)
                    {
                        throw new InvalidDataException(
                            "Real-ESRGAN archive exceeds the 1 GB safety limit.");
                    }

                    await using (var input =
                        await response.Content.ReadAsStreamAsync(token))
                    await using (var output = new FileStream(
                        temp,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        128 * 1024,
                        useAsync: true))
                    {
                        await CopyWithLimitAsync(
                            input,
                            output,
                            MaxEngineArchiveBytes,
                            token);
                    }

                    if (new FileInfo(temp).Length < 1024)
                    {
                        throw new InvalidDataException(
                            "Downloaded Real-ESRGAN archive is unexpectedly small.");
                    }

                    if (!string.IsNullOrWhiteSpace(expectedSha256))
                    {
                        var actual =
                            await Sha256Async(temp, token);

                        if (!actual.Equals(
                                expectedSha256,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                $"Real-ESRGAN archive failed SHA-256 verification. Expected {expectedSha256}, got {actual}.");
                        }
                    }
                },
                cancellationToken,
                attempts: 3);

            File.Move(temp, destination, true);
        }
        catch
        {
            TryDeleteFile(temp);
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
                    $"Download exceeded the {maxBytes:N0}-byte safety limit.");
            }

            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }
    }

    private static async Task<string> Sha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void ExtractSafe(string zipPath, string destination)
        => SafeZip.Extract(zipPath, destination);

    private async Task EnsureRequiredModelsAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ModelsDirectory);

        progress?.Report(
            "Verifying Real-ESRGAN model integrity…");

        var invalid = new List<ModelAsset>();

        foreach (var asset in RequiredModels)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.Combine(
                ModelsDirectory,
                asset.FileName);

            if (!File.Exists(path) ||
                new FileInfo(path).Length != asset.ExpectedSize)
            {
                invalid.Add(asset);
                continue;
            }

            var actualGitBlobSha1 =
                await GitBlobSha1Async(path, cancellationToken);

            if (!actualGitBlobSha1.Equals(
                    asset.ExpectedGitBlobSha1,
                    StringComparison.OrdinalIgnoreCase))
            {
                invalid.Add(asset);
            }
        }

        if (invalid.Count == 0)
        {
            progress?.Report(
                "Real-ESRGAN model files verified.");
            return;
        }

        progress?.Report(
            $"Repairing {invalid.Count} missing or changed Real-ESRGAN model file(s)…");

        var completed = 0;

        foreach (var asset in invalid)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destination = Path.Combine(
                ModelsDirectory,
                asset.FileName);

            await DownloadModelAsync(
                asset,
                destination,
                cancellationToken);

            completed++;

            progress?.Report(
                $"Real-ESRGAN models • {completed}/{invalid.Count} • {asset.FileName}");
        }
    }

    private async Task DownloadModelAsync(
        ModelAsset asset,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(
                asset.Url,
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
                $"Unexpected manager-owned Real-ESRGAN model URL: {asset.Url}");
        }

        var temp = destination + ".download";

        try
        {
            await NetworkRetry.ExecuteAsync(
                async (attempt, token) =>
                {
                    if (attempt > 1)
                        TryDeleteFile(temp);

                    using var response = await _http.GetAsync(
                        uri,
                        HttpCompletionOption.ResponseHeadersRead,
                        token);

                    response.EnsureSuccessStatusCode();

                    if (response.Content.Headers.ContentLength is long contentLength &&
                        contentLength != asset.ExpectedSize)
                    {
                        throw new InvalidDataException(
                            $"Real-ESRGAN model '{asset.FileName}' HTTP size mismatch. Expected {asset.ExpectedSize:N0} bytes, got {contentLength:N0}.");
                    }

                    await using (var input =
                        await response.Content.ReadAsStreamAsync(token))
                    await using (var output = new FileStream(
                        temp,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        128 * 1024,
                        useAsync: true))
                    {
                        await CopyWithLimitAsync(
                            input,
                            output,
                            asset.ExpectedSize,
                            token);
                    }

                    var actualSize = new FileInfo(temp).Length;
                    if (actualSize != asset.ExpectedSize)
                    {
                        throw new InvalidDataException(
                            $"Real-ESRGAN model '{asset.FileName}' size mismatch. Expected {asset.ExpectedSize:N0} bytes, got {actualSize:N0}.");
                    }

                    var actualGitBlobSha1 =
                        await GitBlobSha1Async(temp, token);

                    if (!actualGitBlobSha1.Equals(
                            asset.ExpectedGitBlobSha1,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"Real-ESRGAN model '{asset.FileName}' content fingerprint mismatch.");
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
            TryDeleteFile(temp);
            throw;
        }
    }

    private static async Task<string> GitBlobSha1Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            useAsync: true);

        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA1);

        var header = System.Text.Encoding.ASCII.GetBytes(
            $"blob {stream.Length}\0");
        hash.AppendData(header);

        var buffer = new byte[128 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken);

            if (read == 0)
                break;

            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(
                hash.GetHashAndReset())
            .ToLowerInvariant();
    }

    private bool HasUsableModels(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) ||
            !Directory.Exists(directory))
            return false;

        return RequiredModels.All(asset =>
        {
            var path = Path.Combine(
                directory,
                asset.FileName);

            return File.Exists(path) &&
                   new FileInfo(path).Length == asset.ExpectedSize;
        });
    }

    private List<string> GetMissingModelFiles()
        => RequiredModels
            .Where(asset =>
            {
                var path = Path.Combine(
                    ModelsDirectory,
                    asset.FileName);

                return !File.Exists(path) ||
                       new FileInfo(path).Length != asset.ExpectedSize;
            })
            .Select(asset => asset.FileName)
            .ToList();

    private static void CopyDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);

            // Temporary setup artifacts are managed separately.
            if (relative.Equals(
                    "realesrgan-windows.zip",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static bool IsUsableFile(
        string path,
        long minimumBytes)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !File.Exists(path))
            return false;

        try
        {
            return new FileInfo(path).Length >= minimumBytes;
        }
        catch
        {
            return false;
        }
    }

    private static string MakeUniquePath(string requested)
    {
        if (!File.Exists(requested))
            return requested;

        var directory = Path.GetDirectoryName(requested)!;
        var name = Path.GetFileNameWithoutExtension(requested);
        var extension = Path.GetExtension(requested);

        for (var i = 2; i < 10000; i++)
        {
            var candidate = Path.Combine(directory, $"{name}_{i}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(
            directory,
            $"{name}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{extension}");
    }

    private static string? FindFile(string root, string name)
    {
        if (!Directory.Exists(root))
            return null;

        try
        {
            return Directory
                .EnumerateFiles(root, name, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindDirectory(string root, string name)
    {
        if (!Directory.Exists(root))
            return null;

        try
        {
            return Directory
                .EnumerateDirectories(root, name, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsImage(string path)
        => Path.GetExtension(path).ToLowerInvariant() is
            ".png" or ".jpg" or ".jpeg" or ".bmp" or
            ".tif" or ".tiff" or ".webp";

    private static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> args,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = ExternalProcessTracker.Start(startInfo);

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            return new ProcessResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
        }
        catch (OperationCanceledException)
        {
            ExternalProcessTracker.Kill(process);
            throw;
        }
        finally
        {
            if (!process.HasExited)
                ExternalProcessTracker.Kill(process);
            else
                ExternalProcessTracker.Untrack(process);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
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

    private static string Tail(string value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Length <= max
                ? value.Trim()
                : value[^max..].Trim();

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
    private sealed record ModelAsset(
        string FileName,
        string Url,
        long ExpectedSize,
        string ExpectedGitBlobSha1);

    private sealed record ReleaseAsset(string Tag, string Name, string Url, string? Sha256);
}
