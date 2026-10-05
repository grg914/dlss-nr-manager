using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record MediaProcessOptions(
    string Scale,
    int Style,
    double Intensity,
    string OutputDirectory);

public sealed class MediaService
{
    private const string ProcessorRepo = "DaniilSokolyuk/video2dlssnr";
    private const string ProcessorAsset = "video2dlssnr_release.zip";
    private const string FfmpegAsset = "ffmpeg-master-latest-win64-gpl.zip";
    private const string FfmpegApi =
        "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/tags/latest";

    private readonly HttpClient _http = new();

    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "media-engine");

    private string ProcessorDirectory => Path.Combine(RootDirectory, "video2dlssnr");
    private string ToolsDirectory => Path.Combine(RootDirectory, "tools");
    private string ProcessorExe => FindFile(ProcessorDirectory, "video2dlssnr.exe") ?? "";
    private string FfmpegExe => Path.Combine(ToolsDirectory, "ffmpeg.exe");
    private string FfprobeExe => Path.Combine(ToolsDirectory, "ffprobe.exe");

    public bool IsReady =>
        File.Exists(ProcessorExe) &&
        File.Exists(FfmpegExe) &&
        File.Exists(FfprobeExe);

    public void ResetTools()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
                Directory.Delete(RootDirectory, true);
        }
        catch { }
    }

    public async Task SetupAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ProcessorDirectory);
        Directory.CreateDirectory(ToolsDirectory);

        if (!File.Exists(ProcessorExe))
        {
            progress?.Report("Downloading video2dlssnr…");
            var release = await GetJsonAsync(
                $"https://api.github.com/repos/{ProcessorRepo}/releases/latest",
                cancellationToken);

            var asset = FindAsset(release, ProcessorAsset)
                ?? throw new InvalidOperationException(
                    $"Latest {ProcessorRepo} release has no {ProcessorAsset} asset.");

            var zip = Path.Combine(RootDirectory, ProcessorAsset);
            await DownloadAsync(asset, zip, cancellationToken);
            ExtractSafe(zip, ProcessorDirectory);
            File.Delete(zip);

            if (!File.Exists(ProcessorExe))
                throw new InvalidOperationException(
                    "video2dlssnr.exe was not found after extracting the release.");
        }

        if (!File.Exists(FfmpegExe) || !File.Exists(FfprobeExe))
        {
            progress?.Report("Downloading FFmpeg…");
            var release = await GetJsonAsync(FfmpegApi, cancellationToken);
            var asset = FindAsset(release, FfmpegAsset)
                ?? throw new InvalidOperationException(
                    $"Latest FFmpeg release has no {FfmpegAsset} asset.");

            var zip = Path.Combine(RootDirectory, FfmpegAsset);
            var temp = Path.Combine(RootDirectory, "ffmpeg-extract");
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);

            Directory.CreateDirectory(temp);
            await DownloadAsync(asset, zip, cancellationToken);
            ExtractSafe(zip, temp);

            var ffmpeg = FindFile(temp, "ffmpeg.exe");
            var ffprobe = FindFile(temp, "ffprobe.exe");

            if (ffmpeg == null || ffprobe == null)
                throw new InvalidOperationException(
                    "FFmpeg or ffprobe was not found in the downloaded archive.");

            File.Copy(ffmpeg, FfmpegExe, true);
            File.Copy(ffprobe, FfprobeExe, true);
            Directory.Delete(temp, true);
            File.Delete(zip);
        }

        progress?.Report("Media engine ready.");
    }

    public async Task<string> ProcessAsync(
        string source,
        MediaProcessOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(source))
            throw new FileNotFoundException("Media file was not found.", source);

        if (!IsReady)
            await SetupAsync(progress, cancellationToken);

        Directory.CreateDirectory(options.OutputDirectory);

        return IsImage(source)
            ? await ProcessImageAsync(source, options, progress, cancellationToken)
            : await ProcessVideoAsync(source, options, progress, cancellationToken);
    }

    private async Task<string> ProcessImageAsync(
        string source,
        MediaProcessOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Neural Rendering image…");

        var args = new List<string>
        {
            "--nr-run",
            "--in", source,
            "--out", options.OutputDirectory,
            "--nr-style", Math.Clamp(options.Style, 0, 2).ToString(),
            "--nr-intensity", Math.Clamp(options.Intensity, 0, 2)
                .ToString("0.###", CultureInfo.InvariantCulture),
            "--nr-ui-correction", "0"
        };

        AppendScaleArgs(args, options.Scale);

        var result = await RunAsync(
            ProcessorExe,
            args,
            Path.GetDirectoryName(ProcessorExe)!,
            cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Image processing failed.\n{Tail(result.Error, 2500)}");

        var expected = Path.Combine(
            options.OutputDirectory,
            Path.GetFileNameWithoutExtension(source) + "_nr.png");

        progress?.Report("Image complete.");
        return File.Exists(expected)
            ? expected
            : Directory.EnumerateFiles(options.OutputDirectory, "*_nr.*")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
              ?? throw new InvalidOperationException("The processor produced no output image.");
    }

    private async Task<string> ProcessVideoAsync(
        string source,
        MediaProcessOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Reading video metadata…");
        var probe = await ProbeAsync(source, cancellationToken);
        var (outWidth, outHeight) = ResolveOutputSize(
            probe.Width,
            probe.Height,
            options.Scale);

        var output = Path.Combine(
            options.OutputDirectory,
            Path.GetFileNameWithoutExtension(source) + "_nr.mp4");

        var decodeArgs = new[]
        {
            "-hide_banner", "-loglevel", "error",
            "-i", source,
            "-vf", "format=rgba",
            "-f", "rawvideo", "-"
        };

        var processorArgs = new List<string>
        {
            "--nr-video",
            "--nr-in", $"{probe.Width}x{probe.Height}",
            "--nr-style", Math.Clamp(options.Style, 0, 2).ToString(),
            "--nr-intensity", Math.Clamp(options.Intensity, 0, 2)
                .ToString("0.###", CultureInfo.InvariantCulture),
            "--nr-ui-correction", "0",
            "--nr-motion", "1"
        };

        if ((outWidth, outHeight) != (probe.Width, probe.Height))
        {
            processorArgs.Add("--nr-width");
            processorArgs.Add(outWidth.ToString());
            processorArgs.Add("--nr-height");
            processorArgs.Add(outHeight.ToString());
        }

        var encodeArgs = new[]
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "rawvideo",
            "-pix_fmt", "rgba",
            "-s", $"{outWidth}x{outHeight}",
            "-r", probe.Fps.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", "-",
            "-i", source,
            "-map", "0:v:0",
            "-map", "1:a:0?",
            "-c:v", "hevc_nvenc",
            "-preset", "p5",
            "-cq", "19",
            "-pix_fmt", "p010le",
            "-c:a", "aac",
            "-b:a", "192k",
            "-movflags", "+faststart",
            "-shortest",
            output
        };

        progress?.Report(
            $"Processing video {probe.Width}×{probe.Height} → {outWidth}×{outHeight}…");

        using var decoder = StartPipeProcess(FfmpegExe, decodeArgs, ToolsDirectory);
        using var processor = StartPipeProcess(
            ProcessorExe,
            processorArgs,
            Path.GetDirectoryName(ProcessorExe)!);
        using var encoder = StartPipeProcess(FfmpegExe, encodeArgs, ToolsDirectory);

        var decodeToProcessor = decoder.StandardOutput.BaseStream
            .CopyToAsync(processor.StandardInput.BaseStream, cancellationToken)
            .ContinueWith(_ =>
            {
                try { processor.StandardInput.Close(); } catch { }
            }, CancellationToken.None);

        var processorToEncoder = processor.StandardOutput.BaseStream
            .CopyToAsync(encoder.StandardInput.BaseStream, cancellationToken)
            .ContinueWith(_ =>
            {
                try { encoder.StandardInput.Close(); } catch { }
            }, CancellationToken.None);

        var decoderErrorTask = decoder.StandardError.ReadToEndAsync(cancellationToken);
        var processorErrorTask = processor.StandardError.ReadToEndAsync(cancellationToken);
        var encoderErrorTask = encoder.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(decodeToProcessor, processorToEncoder);
        await Task.WhenAll(
            decoder.WaitForExitAsync(cancellationToken),
            processor.WaitForExitAsync(cancellationToken),
            encoder.WaitForExitAsync(cancellationToken));

        var decoderError = await decoderErrorTask;
        var processorError = await processorErrorTask;
        var encoderError = await encoderErrorTask;

        if (decoder.ExitCode != 0)
            throw new InvalidOperationException(
                $"Video decode failed.\n{Tail(decoderError, 2500)}");

        if (processor.ExitCode != 0)
            throw new InvalidOperationException(
                $"Neural Rendering failed.\n{Tail(processorError, 2500)}");

        if (encoder.ExitCode != 0)
            throw new InvalidOperationException(
                $"Video encoding failed.\n{Tail(encoderError, 2500)}");

        if (!File.Exists(output))
            throw new InvalidOperationException("The processor produced no output video.");

        progress?.Report("Video complete.");
        return output;
    }

    private async Task<VideoProbe> ProbeAsync(
        string source,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            FfprobeExe,
            new[]
            {
                "-v", "error",
                "-select_streams", "v:0",
                "-show_entries", "stream=width,height,r_frame_rate",
                "-of", "json",
                source
            },
            ToolsDirectory,
            cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"ffprobe failed.\n{Tail(result.Error, 1800)}");

        using var json = JsonDocument.Parse(result.Output);
        var stream = json.RootElement.GetProperty("streams")[0];
        var width = stream.GetProperty("width").GetInt32();
        var height = stream.GetProperty("height").GetInt32();
        var rate = stream.GetProperty("r_frame_rate").GetString() ?? "30/1";
        var parts = rate.Split('/');
        var fps = parts.Length == 2 &&
                  double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
                  double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
                  den != 0
            ? num / den
            : 30;

        return new VideoProbe(width, height, fps);
    }

    private static (int Width, int Height) ResolveOutputSize(
        int width,
        int height,
        string scale)
    {
        if (scale.Equals("2x", StringComparison.OrdinalIgnoreCase))
            return (Even(width * 2), Even(height * 2));

        if (scale.Equals("4K", StringComparison.OrdinalIgnoreCase))
        {
            if (width >= height)
                return (3840, Even((int)Math.Round(height * (3840d / width))));

            return (Even((int)Math.Round(width * (3840d / height))), 3840);
        }

        return (Even(width), Even(height));
    }

    private static void AppendScaleArgs(List<string> args, string scale)
    {
        if (scale.Equals("2x", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--nr-scale");
            args.Add("2");
        }
        else if (scale.Equals("4K", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--nr-width");
            args.Add("3840");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", "0.7"));

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string? FindAsset(JsonDocument release, string assetName)
    {
        if (!release.RootElement.TryGetProperty("assets", out var assets))
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name) ||
                !asset.TryGetProperty("browser_download_url", out var url))
                continue;

            if (string.Equals(
                    name.GetString(),
                    assetName,
                    StringComparison.OrdinalIgnoreCase))
                return url.GetString();
        }

        return null;
    }

    private async Task DownloadAsync(
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", "0.7"));

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            useAsync: true);

        await input.CopyToAsync(output, cancellationToken);
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

            var target = Path.GetFullPath(
                Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Archive entry escapes destination: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private static string? FindFile(string root, string name)
    {
        if (!Directory.Exists(root))
            return null;

        try
        {
            return Directory.EnumerateFiles(root, name, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsImage(string path)
        => Path.GetExtension(path).ToLowerInvariant() is
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" or ".webp";

    private static Process StartPipeProcess(
        string exe,
        IEnumerable<string> args,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(exe)}.");
    }

    private static async Task<ProcessResult> RunAsync(
        string exe,
        IEnumerable<string> args,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(exe)
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
            ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(exe)}.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static int Even(int value) => Math.Max(2, value & ~1);

    private static string Tail(string value, int max)
        => value.Length <= max ? value : value[^max..];

    private sealed record VideoProbe(int Width, int Height, double Fps);
    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
