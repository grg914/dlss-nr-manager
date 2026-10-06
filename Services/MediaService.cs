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
    private const long MaxComponentDownloadBytes = 1024L * 1024 * 1024;
    private const long MaxExtractedArchiveBytes = 4L * 1024 * 1024 * 1024;
    private const int MaxArchiveEntries = 100_000;

    private readonly HttpClient _http = new();

    public MediaService()
    {
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "DlssNrManager",
                AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));
    }

    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "media-engine");

    private string ProcessorDirectory => Path.Combine(RootDirectory, "video2dlssnr");
    private string ToolsDirectory => Path.Combine(RootDirectory, "tools");
    private string ProcessorExe => FindFile(ProcessorDirectory, "video2dlssnr.exe") ?? "";
    private string FfmpegExe => Path.Combine(ToolsDirectory, "ffmpeg.exe");
    private string FfprobeExe => Path.Combine(ToolsDirectory, "ffprobe.exe");

    public string FfmpegPath => FfmpegExe;
    public string FfprobePath => FfprobeExe;

    public bool IsReady =>
        IsUsableFile(ProcessorExe, 64 * 1024) &&
        IsUsableFile(FfmpegExe, 1024 * 1024) &&
        IsUsableFile(FfprobeExe, 1024 * 1024);

    public void ResetTools()
    {
        // Keep sibling engines such as media-engine/realesrgan intact.
        // Component updates only own video2dlssnr and FFmpeg tools.
        TryDeleteDirectory(ProcessorDirectory);
        TryDeleteDirectory(ToolsDirectory);
    }

    public async Task UpdateToolsAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootDirectory);

        var backupRoot = Path.Combine(
            RootDirectory,
            "_update-backup-" + Guid.NewGuid().ToString("N"));
        var processorBackup = Path.Combine(backupRoot, "video2dlssnr");
        var toolsBackup = Path.Combine(backupRoot, "tools");
        var processorMoved = false;
        var toolsMoved = false;

        try
        {
            Directory.CreateDirectory(backupRoot);

            if (Directory.Exists(ProcessorDirectory))
            {
                Directory.Move(ProcessorDirectory, processorBackup);
                processorMoved = true;
            }

            if (Directory.Exists(ToolsDirectory))
            {
                Directory.Move(ToolsDirectory, toolsBackup);
                toolsMoved = true;
            }

            await SetupAsync(progress, cancellationToken);
            TryDeleteDirectory(backupRoot);
        }
        catch
        {
            // Delete only replacement directories whose originals were
            // successfully moved away. If a move itself failed, leave the
            // original directory untouched.
            if (processorMoved)
                TryDeleteDirectory(ProcessorDirectory);

            if (toolsMoved)
                TryDeleteDirectory(ToolsDirectory);

            if (processorMoved && Directory.Exists(processorBackup))
                Directory.Move(processorBackup, ProcessorDirectory);

            if (toolsMoved && Directory.Exists(toolsBackup))
                Directory.Move(toolsBackup, ToolsDirectory);

            TryDeleteDirectory(backupRoot);
            throw;
        }
    }

    public async Task SetupAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ProcessorDirectory);
        Directory.CreateDirectory(ToolsDirectory);

        if (!IsUsableFile(ProcessorExe, 64 * 1024))
        {
            progress?.Report("Downloading video2dlssnr…");
            var release = await GetJsonAsync(
                $"https://api.github.com/repos/{ProcessorRepo}/releases/latest",
                cancellationToken);

            var asset = FindAsset(release, ProcessorAsset)
                ?? throw new InvalidOperationException(
                    $"Latest {ProcessorRepo} release has no {ProcessorAsset} asset.");

            var zip = Path.Combine(RootDirectory, ProcessorAsset);
            var extract = Path.Combine(
                RootDirectory,
                "video2dlssnr-extract-" + Guid.NewGuid().ToString("N"));

            try
            {
                await DownloadAsync(
                    asset.Url,
                    zip,
                    asset.Sha256,
                    cancellationToken);

                ExtractSafe(zip, extract);

                var extractedExe =
                    FindFile(extract, "video2dlssnr.exe");

                if (!IsUsableFile(extractedExe ?? "", 64 * 1024))
                {
                    throw new InvalidOperationException(
                        "video2dlssnr.exe was not found or is invalid after extracting the release.");
                }

                TryDeleteDirectory(ProcessorDirectory);
                Directory.Move(extract, ProcessorDirectory);
            }
            finally
            {
                TryDeleteFile(zip);
                TryDeleteDirectory(extract);
            }

            if (!IsUsableFile(ProcessorExe, 64 * 1024))
            {
                throw new InvalidOperationException(
                    "video2dlssnr installation did not produce a usable executable.");
            }
        }

        if (!IsUsableFile(FfmpegExe, 1024 * 1024) ||
            !IsUsableFile(FfprobeExe, 1024 * 1024))
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
            await DownloadAsync(
                asset.Url,
                zip,
                asset.Sha256,
                cancellationToken);
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

        try
        {
            await Task.WhenAll(decodeToProcessor, processorToEncoder);
            await Task.WhenAll(
                decoder.WaitForExitAsync(cancellationToken),
                processor.WaitForExitAsync(cancellationToken),
                encoder.WaitForExitAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            ExternalProcessTracker.Kill(decoder);
            ExternalProcessTracker.Kill(processor);
            ExternalProcessTracker.Kill(encoder);
            throw;
        }
        finally
        {
            if (!decoder.HasExited) ExternalProcessTracker.Kill(decoder); else ExternalProcessTracker.Untrack(decoder);
            if (!processor.HasExited) ExternalProcessTracker.Kill(processor); else ExternalProcessTracker.Untrack(processor);
            if (!encoder.HasExited) ExternalProcessTracker.Kill(encoder); else ExternalProcessTracker.Untrack(encoder);
        }

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
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static ReleaseAsset? FindAsset(
        JsonDocument release,
        string assetName)
    {
        if (!release.RootElement.TryGetProperty(
                "assets",
                out var assets))
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name) ||
                !asset.TryGetProperty(
                    "browser_download_url",
                    out var url))
                continue;

            if (!string.Equals(
                    name.GetString(),
                    assetName,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var downloadUrl = url.GetString();
            if (string.IsNullOrWhiteSpace(downloadUrl))
                return null;

            string? sha256 = null;
            if (asset.TryGetProperty(
                    "digest",
                    out var digestElement))
            {
                var digest = digestElement.GetString();
                if (!string.IsNullOrWhiteSpace(digest) &&
                    digest.StartsWith(
                        "sha256:",
                        StringComparison.OrdinalIgnoreCase))
                {
                    sha256 = digest["sha256:".Length..];
                }
            }

            return new ReleaseAsset(
                downloadUrl,
                sha256);
        }

        return null;
    }

    private async Task DownloadAsync(
        string url,
        string destination,
        string? expectedSha256,
        CancellationToken cancellationToken)
    {
        var temp = destination + ".download";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(
                "github.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unexpected media-component release URL: {url}");
        }

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

                    if (response.Content.Headers.ContentLength is > MaxComponentDownloadBytes)
                    {
                        throw new InvalidDataException(
                            "Media component archive exceeds the 1 GB safety limit.");
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
                            MaxComponentDownloadBytes,
                            token);
                    }

                    if (new FileInfo(temp).Length < 1024)
                    {
                        throw new InvalidDataException(
                            "Downloaded media component archive is unexpectedly small.");
                    }

                    if (!string.IsNullOrWhiteSpace(expectedSha256))
                    {
                        var actual =
                            await HashService.Sha256Async(
                                temp,
                                token);

                        if (!actual.Equals(
                                expectedSha256,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                $"Media component SHA-256 mismatch. Expected {expectedSha256}, got {actual}.");
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

    private static void ExtractSafe(
        string zipPath,
        string destination)
        => SafeZip.Extract(
            zipPath,
            destination);

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

        return ExternalProcessTracker.Start(startInfo);
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

    private static int Even(int value) => Math.Max(2, value & ~1);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
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

    private static string Tail(string value, int max)
        => value.Length <= max ? value : value[^max..];

    private sealed record ReleaseAsset(
        string Url,
        string? Sha256);

    private sealed record VideoProbe(int Width, int Height, double Fps);
    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
