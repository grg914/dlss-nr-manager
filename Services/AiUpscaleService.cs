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

    private readonly HttpClient _http = new();

    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "media-engine",
        "realesrgan");

    private string EngineExe =>
        FindFile(RootDirectory, "realesrgan-ncnn-vulkan.exe") ?? "";

    private string ModelsDirectory =>
        Directory.Exists(Path.Combine(RootDirectory, "models"))
            ? Path.Combine(RootDirectory, "models")
            : FindDirectory(RootDirectory, "models") ?? "";

    private static string MediaRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "media-engine");

    private static string FfmpegExe => Path.Combine(MediaRoot, "tools", "ffmpeg.exe");
    private static string FfprobeExe => Path.Combine(MediaRoot, "tools", "ffprobe.exe");

    public bool IsReady =>
        File.Exists(EngineExe) &&
        HasUsableModels(ModelsDirectory);

    public AiUpscaleService()
    {
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", "1.0"));
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

        progress?.Report("Checking latest Real-ESRGAN NCNN Vulkan release…");
        using var release = await GetJsonAsync(
            $"https://api.github.com/repos/{Repo}/releases/latest",
            cancellationToken);

        var asset = FindWindowsAsset(release)
            ?? throw new InvalidOperationException(
                "No Windows ZIP was found in the latest Real-ESRGAN NCNN Vulkan release.");

        var zipPath = Path.Combine(RootDirectory, "realesrgan-windows.zip");
        var extractDir = Path.Combine(RootDirectory, "_extract");

        TryDeleteDirectory(extractDir);
        Directory.CreateDirectory(extractDir);

        progress?.Report($"Downloading Real-ESRGAN {asset.Tag}…");
        await DownloadAsync(asset.Url, zipPath, asset.Sha256, cancellationToken);

        progress?.Report("Extracting AI Upscale engine…");
        ExtractSafe(zipPath, extractDir);

        var exe = FindFile(extractDir, "realesrgan-ncnn-vulkan.exe")
            ?? throw new InvalidOperationException(
                "Real-ESRGAN executable was not found after extraction.");

        // Copy the complete extracted package, not only the directory that
        // contains the executable. Some Real-ESRGAN release layouts keep
        // models/ beside the executable directory rather than underneath it.
        CopyDirectoryContents(extractDir, RootDirectory);

        // A few repackaged distributions keep the models in models.zip.
        // Expand it when present so the engine can use the standard -m path.
        if (!HasUsableModels(ModelsDirectory))
        {
            var modelsArchive = FindFile(RootDirectory, "models.zip");
            if (modelsArchive != null)
            {
                var modelDestination = Path.Combine(
                    Path.GetDirectoryName(modelsArchive)!,
                    "models");

                Directory.CreateDirectory(modelDestination);
                ExtractSafe(modelsArchive, modelDestination);
            }
        }

        TryDeleteDirectory(extractDir);
        TryDeleteFile(zipPath);

        if (!IsReady)
        {
            var discoveredExe = EngineExe;
            var discoveredModels = ModelsDirectory;

            throw new InvalidOperationException(
                "Real-ESRGAN installation completed but the engine package is incomplete. " +
                $"Executable: {(File.Exists(discoveredExe) ? discoveredExe : "not found")}; " +
                $"models: {(HasUsableModels(discoveredModels) ? discoveredModels : "not found or incomplete")}. " +
                "Use 'Set up AI Upscale engine' again to repair the installation.");
        }

        progress?.Report($"AI Upscale engine ready • Real-ESRGAN {asset.Tag}.");
    }

    public void Reset()
        => TryDeleteDirectory(RootDirectory);

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
                    "-vsync", "0",
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
        using var response = await _http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(
                         destination,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         128 * 1024,
                         useAsync: true))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        if (new FileInfo(destination).Length < 1024)
            throw new InvalidDataException("Downloaded Real-ESRGAN archive is unexpectedly small.");

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actual = await Sha256Async(destination, cancellationToken);
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(destination);
                throw new InvalidDataException(
                    $"Real-ESRGAN archive failed SHA-256 verification. Expected {expectedSha256}, got {actual}.");
            }
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
    {
        var destinationRoot =
            Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var zip = ZipFile.OpenRead(zipPath);

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var target = Path.GetFullPath(Path.Combine(
                destination,
                entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Archive entry escapes destination: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static bool HasUsableModels(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        try
        {
            var files = Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // At least one complete NCNN model pair is enough to consider the
            // engine installed. Individual model selection is validated later
            // by Real-ESRGAN itself.
            return files.Any(name =>
                name!.EndsWith(".param", StringComparison.OrdinalIgnoreCase) &&
                files.Contains(
                    Path.ChangeExtension(name, ".bin")));
        }
        catch
        {
            return false;
        }
    }

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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start {Path.GetFileName(executable)}.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
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
    private sealed record ReleaseAsset(string Tag, string Name, string Url, string? Sha256);
}
