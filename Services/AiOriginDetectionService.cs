using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DlssNrManager.Services;

public sealed record AiOriginDetectionResult(
    string Verdict,
    double AiProbability,
    double Confidence,
    int FramesAnalyzed,
    int FramesFlagged,
    IReadOnlyList<string> ProvenanceSignals,
    string Model,
    string Notes)
{
    public string Summary =>
        $"{Verdict} • AI probability {AiProbability:P0} • confidence {Confidence:P0}" +
        (FramesAnalyzed > 1 ? $" • frames {FramesFlagged}/{FramesAnalyzed} flagged" : "");
}

public sealed class AiOriginDetectionService : IDisposable
{
    private const string ModelName = "CapCheck ViT AI-vs-Real (INT8 ONNX)";
    private const string ModelUrl =
        "https://huggingface.co/onnx-community/ai-image-detection-ONNX/resolve/main/onnx/model_int8.onnx?download=true";
    private const string ModelSha256 =
        "08B349F1B535F2F0CC2A8610BBF57C27593A0364E78B6C91205C0FF2BF29D714";

    private static readonly string[] AiMarkers =
    [
        "c2pa", "content credentials", "openai", "dall-e", "dall·e",
        "midjourney", "stable diffusion", "automatic1111", "comfyui",
        "invokeai", "adobe firefly", "generative fill", "sora", "runway",
        "flux", "ideogram"
    ];

    private readonly MediaService _media;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(20) };
    private InferenceSession? _session;

    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "ai-origin-detector");

    public string ModelPath => Path.Combine(RootDirectory, "ai-image-detector-int8.onnx");

    public bool IsReady => File.Exists(ModelPath);

    public AiOriginDetectionService(MediaService media)
    {
        _media = media;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DlssNrManager/1.2");
    }

    public async Task SetupAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootDirectory);

        if (File.Exists(ModelPath))
        {
            var existing = await Sha256Async(ModelPath, cancellationToken);
            if (existing.Equals(ModelSha256, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report("AI origin detector ready.");
                return;
            }

            TryDeleteFile(ModelPath);
        }

        progress?.Report("Downloading local AI-origin detection model (~87 MB)…");
        var temp = ModelPath + ".download";

        try
        {
            using var response = await _http.GetAsync(
                ModelUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                             temp,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            var actual = await Sha256Async(temp, cancellationToken);
            if (!actual.Equals(ModelSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"AI detector SHA-256 mismatch. Expected {ModelSha256}, got {actual}.");

            File.Move(temp, ModelPath, true);
            progress?.Report("AI origin detector ready • local ONNX model installed.");
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    public async Task<AiOriginDetectionResult> AnalyzeAsync(
        string source,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(source))
            throw new FileNotFoundException("Media file was not found.", source);

        if (!IsReady)
            await SetupAsync(progress, cancellationToken);

        var extension = Path.GetExtension(source).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" or ".webp"
            ? await AnalyzeImageAsync(source, progress, cancellationToken)
            : await AnalyzeVideoAsync(source, progress, cancellationToken);
    }

    private async Task<AiOriginDetectionResult> AnalyzeImageAsync(
        string source,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Analyzing image provenance and visual AI signals…");

        var provenance = await FindImageProvenanceSignalsAsync(source, cancellationToken);
        var ai = await ClassifyImageAsync(source, cancellationToken);

        return BuildResult([ai], provenance);
    }

    private async Task<AiOriginDetectionResult> AnalyzeVideoAsync(
        string source,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Preparing video analysis…");
        await _media.SetupAsync(progress, cancellationToken);

        var duration = await ProbeDurationAsync(source, cancellationToken);
        var provenance = await FindVideoProvenanceSignalsAsync(source, cancellationToken);
        var session = Path.Combine(
            RootDirectory,
            "frames-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session);

        try
        {
            const int targetFrames = 24;
            var fps = duration > 0
                ? Math.Clamp(targetFrames / duration, 0.05, 8.0)
                : 1.0;

            progress?.Report($"Sampling up to {targetFrames} frames across the video…");

            var extraction = await RunAsync(
                _media.FfmpegPath,
                [
                    "-y",
                    "-hide_banner",
                    "-loglevel", "error",
                    "-i", source,
                    "-vf", $"fps={fps.ToString("0.########", CultureInfo.InvariantCulture)}",
                    "-frames:v", targetFrames.ToString(CultureInfo.InvariantCulture),
                    Path.Combine(session, "%04d.png")
                ],
                Path.GetDirectoryName(_media.FfmpegPath)!,
                cancellationToken);

            if (extraction.ExitCode != 0)
                throw new InvalidOperationException(
                    "FFmpeg video sampling failed.\n" + Tail(extraction.Error, 2200));

            var frames = Directory
                .EnumerateFiles(session, "*.png")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (frames.Count == 0)
                throw new InvalidOperationException("No frames were extracted for AI-origin analysis.");

            var scores = new List<double>(frames.Count);
            for (var i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"AI-origin analysis • frame {i + 1}/{frames.Count}");
                scores.Add(await ClassifyImageAsync(frames[i], cancellationToken));
            }

            return BuildResult(scores, provenance);
        }
        finally
        {
            TryDeleteDirectory(session);
        }
    }

    private AiOriginDetectionResult BuildResult(
        IReadOnlyList<double> scores,
        IReadOnlyList<string> provenance)
    {
        var average = scores.Count == 0 ? 0.5 : scores.Average();
        var flagged = scores.Count(x => x >= 0.65);
        var flaggedRatio = scores.Count == 0 ? 0 : (double)flagged / scores.Count;
        var spread = scores.Count <= 1 ? 0 : scores.Max() - scores.Min();

        string verdict;
        if (provenance.Any(IsStrongAiMarker))
            verdict = "AI provenance / generator metadata detected";
        else if (scores.Count > 3 && spread >= 0.55 && flaggedRatio is > 0.20 and < 0.80)
            verdict = "Mixed / uncertain synthetic-media signals";
        else if (average >= 0.75)
            verdict = "Likely AI-generated";
        else if (average <= 0.25)
            verdict = "Likely conventional / camera-origin";
        else
            verdict = "Uncertain";

        var consistency = scores.Count <= 1
            ? 1.0
            : Math.Abs(flaggedRatio - 0.5) * 2.0;
        var modelConfidence = Math.Clamp(Math.Abs(average - 0.5) * 2.0, 0, 1);
        var confidence = Math.Clamp(
            0.75 * modelConfidence + 0.25 * consistency,
            0,
            1);

        if (provenance.Any(IsStrongAiMarker))
            confidence = Math.Max(confidence, 0.90);

        var notes =
            "Probabilistic screening only. A negative result does not prove a file is human-made. " +
            "Compression, screenshots, heavy editing, new generators and non-photographic art can change accuracy.";

        return new AiOriginDetectionResult(
            verdict,
            average,
            confidence,
            scores.Count,
            flagged,
            provenance,
            ModelName,
            notes);
    }

    private static bool IsStrongAiMarker(string marker)
        => !marker.Equals("C2PA marker present", StringComparison.OrdinalIgnoreCase);

    private async Task<double> ClassifyImageAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            var session = GetSession();
            var tensor = CreateImageTensor(path);
            var inputName = session.InputMetadata.Keys.First();

            var input = NamedOnnxValue.CreateFromTensor(inputName, tensor);
            using var results = session.Run([input]);
            var output = results.First().AsEnumerable<float>().ToArray();

            if (output.Length < 2)
                throw new InvalidDataException("AI detector returned an unexpected output tensor.");

            var max = output.Max();
            var exp0 = Math.Exp(output[0] - max);
            var exp1 = Math.Exp(output[1] - max);
            return exp1 / (exp0 + exp1);
        }, cancellationToken);
    }

    private InferenceSession GetSession()
    {
        if (_session != null)
            return _session;

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
            InterOpNumThreads = 1
        };

        _session = new InferenceSession(ModelPath, options);
        return _session;
    }

    private static DenseTensor<float> CreateImageTensor(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];

        var scale = new ScaleTransform(
            224d / Math.Max(1, source.PixelWidth),
            224d / Math.Max(1, source.PixelHeight));
        var resized = new TransformedBitmap(source, scale);
        var converted = new FormatConvertedBitmap(
            resized,
            PixelFormats.Bgra32,
            null,
            0);

        const int width = 224;
        const int height = 224;
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);

        var tensor = new DenseTensor<float>([1, 3, height, width]);
        var plane = width * height;

        for (var i = 0; i < plane; i++)
        {
            var b = pixels[i * 4];
            var g = pixels[i * 4 + 1];
            var r = pixels[i * 4 + 2];

            tensor.Buffer.Span[i] = (r / 255f - 0.5f) / 0.5f;
            tensor.Buffer.Span[plane + i] = (g / 255f - 0.5f) / 0.5f;
            tensor.Buffer.Span[plane * 2 + i] = (b / 255f - 0.5f) / 0.5f;
        }

        return tensor;
    }

    private async Task<IReadOnlyList<string>> FindImageProvenanceSignalsAsync(
        string source,
        CancellationToken cancellationToken)
    {
        const int maxBytes = 8 * 1024 * 1024;
        await using var stream = File.OpenRead(source);
        var length = (int)Math.Min(stream.Length, maxBytes);
        var buffer = new byte[length];
        var read = await stream.ReadAsync(buffer.AsMemory(0, length), cancellationToken);
        var text = Encoding.Latin1.GetString(buffer, 0, read).ToLowerInvariant();
        return FindMarkers(text);
    }

    private async Task<IReadOnlyList<string>> FindVideoProvenanceSignalsAsync(
        string source,
        CancellationToken cancellationToken)
    {
        var probe = await RunAsync(
            _media.FfprobePath,
            [
                "-v", "quiet",
                "-show_format",
                "-show_streams",
                "-of", "json",
                source
            ],
            Path.GetDirectoryName(_media.FfprobePath)!,
            cancellationToken);

        return FindMarkers((probe.Output + "\n" + probe.Error).ToLowerInvariant());
    }

    private static IReadOnlyList<string> FindMarkers(string text)
    {
        var found = new List<string>();
        foreach (var marker in AiMarkers)
        {
            if (!text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                continue;

            found.Add(marker.Equals("c2pa", StringComparison.OrdinalIgnoreCase)
                ? "C2PA marker present"
                : $"Generator metadata: {marker}");
        }

        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<double> ProbeDurationAsync(
        string source,
        CancellationToken cancellationToken)
    {
        var probe = await RunAsync(
            _media.FfprobePath,
            [
                "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1:nokey=1",
                source
            ],
            Path.GetDirectoryName(_media.FfprobePath)!,
            cancellationToken);

        return double.TryParse(
            probe.Output.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var duration)
            ? duration
            : 0;
    }

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

    private static async Task<string> Sha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string Tail(string value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Length <= max
                ? value.Trim()
                : value[^max..].Trim();

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _http.Dispose();
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
