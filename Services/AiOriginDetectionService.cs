using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
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
    string Notes,
    double PrimaryModelProbability = 0,
    double SecondaryModelProbability = 0,
    double ModelDisagreement = 0)
{
    public string Summary =>
        $"{Verdict} • ensemble AI score {AiProbability:P0} • confidence {Confidence:P0}" +
        (FramesAnalyzed > 1 ? $" • strong-AI frames {FramesFlagged}/{FramesAnalyzed}" : "");
}

public sealed class AiOriginDetectionService : IDisposable
{
    private const string PrimaryModelName = "CapCheck ViT AI-vs-Real";
    private const string PrimaryModelUrl =
        "https://huggingface.co/onnx-community/ai-image-detection-ONNX/resolve/e3cfe99f2841930a040a6281682c10c989965603/onnx/model_int8.onnx?download=true";
    private const string PrimaryModelSha256 =
        "08B349F1B535F2F0CC2A8610BBF57C27593A0364E78B6C91205C0FF2BF29D714";

    private const string SecondaryModelName = "AI Image Detect Distilled ViT";
    private const string SecondaryModelUrl =
        "https://huggingface.co/onnx-community/ai-image-detect-distilled-ONNX/resolve/7f067e23521eeb6d6525221af82c613fb746aaff/onnx/model_int8.onnx?download=true";
    private const string SecondaryModelSha256 =
        "7273CB9CD81E17EAE04771010D2199BA6AE34EA2A75A275518C0BC4A2C26FFD2";

    private static readonly string[] AiMarkers =
    [
        "openai", "dall-e", "dall·e", "midjourney", "stable diffusion",
        "automatic1111", "comfyui", "invokeai", "adobe firefly",
        "generative fill", "sora", "runway", "flux", "ideogram"
    ];

    private static readonly string[] ProvenanceMarkers =
    [
        "c2pa", "content credentials", "jumb", "c2pa.claim"
    ];

    private readonly MediaService _media;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(20) };
    private InferenceSession? _primarySession;
    private InferenceSession? _secondarySession;

    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "ai-origin-detector");

    public string PrimaryModelPath => Path.Combine(RootDirectory, "ai-image-detector-int8.onnx");
    public string SecondaryModelPath => Path.Combine(RootDirectory, "ai-image-detector-distilled-int8.onnx");

    public bool IsReady => File.Exists(PrimaryModelPath) && File.Exists(SecondaryModelPath);

    public AiOriginDetectionService(MediaService media)
    {
        _media = media;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"DlssNrManager/{AppIdentity.UserAgentVersion}");
    }

    public async Task SetupAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootDirectory);

        await EnsureModelAsync(
            PrimaryModelPath,
            PrimaryModelUrl,
            PrimaryModelSha256,
            "primary detector (~87 MB)",
            progress,
            cancellationToken);

        await EnsureModelAsync(
            SecondaryModelPath,
            SecondaryModelUrl,
            SecondaryModelSha256,
            "secondary cross-check detector (~15 MB)",
            progress,
            cancellationToken);

        progress?.Report("AI origin detector ready • 2-model local ensemble installed.");
    }

    private async Task EnsureModelAsync(
        string destination,
        string url,
        string expectedSha256,
        string label,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
        {
            var existing = await Sha256Async(destination, cancellationToken);
            if (existing.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                return;

            TryDeleteFile(destination);
        }

        progress?.Report($"Downloading {label}…");
        var temp = destination + ".download";

        try
        {
            using var response = await _http.GetAsync(
                url,
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
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"AI detector SHA-256 mismatch. Expected {expectedSha256}, got {actual}.");

            File.Move(temp, destination, true);
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
        progress?.Report("Analyzing provenance + two independent visual detectors…");

        var provenance = await FindImageProvenanceSignalsAsync(source, cancellationToken);
        var score = await ClassifyEnsembleAsync(source, cancellationToken);

        return BuildResult([score], provenance, isVideo: false);
    }

    private async Task<AiOriginDetectionResult> AnalyzeVideoAsync(
        string source,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Preparing multi-frame video analysis…");
        await _media.SetupAsync(progress, cancellationToken);

        var duration = await ProbeDurationAsync(source, cancellationToken);
        var provenance = await FindVideoProvenanceSignalsAsync(source, cancellationToken);
        var session = Path.Combine(
            RootDirectory,
            "frames-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session);

        try
        {
            const int targetFrames = 32;
            var fps = duration > 0
                ? Math.Clamp(targetFrames / duration, 0.025, 6.0)
                : 1.0;

            progress?.Report($"Sampling up to {targetFrames} frames across the full video…");

            var extraction = await RunAsync(
                _media.FfmpegPath,
                [
                    "-y",
                    "-hide_banner",
                    "-loglevel", "error",
                    "-i", source,
                    "-vf", $"fps={fps.ToString("0.########", CultureInfo.InvariantCulture)}",
                    "-frames:v", targetFrames.ToString(CultureInfo.InvariantCulture),
                    "-fps_mode", "passthrough",
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

            var scores = new List<FrameScore>(frames.Count);
            for (var i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"AI ensemble analysis • frame {i + 1}/{frames.Count}");
                scores.Add(await ClassifyEnsembleAsync(frames[i], cancellationToken));
            }

            return BuildResult(scores, provenance, isVideo: true);
        }
        finally
        {
            TryDeleteDirectory(session);
        }
    }

    private AiOriginDetectionResult BuildResult(
        IReadOnlyList<FrameScore> scores,
        IReadOnlyList<string> provenance,
        bool isVideo)
    {
        if (scores.Count == 0)
            throw new InvalidOperationException("AI detector produced no scores.");

        var primary = Median(scores.Select(x => x.PrimaryAi).ToList());
        var secondary = Median(scores.Select(x => x.SecondaryAi).ToList());
        var disagreement = Math.Abs(primary - secondary);

        // Geometric mean intentionally punishes a one-model false positive.
        var ensemble = Math.Sqrt(Math.Clamp(primary, 0, 1) * Math.Clamp(secondary, 0, 1));

        var strongAiFrames = scores.Count(x => x.PrimaryAi >= 0.80 && x.SecondaryAi >= 0.80);
        var strongRealFrames = scores.Count(x => x.PrimaryAi <= 0.35 && x.SecondaryAi <= 0.35);
        var strongAiRatio = (double)strongAiFrames / scores.Count;
        var strongRealRatio = (double)strongRealFrames / scores.Count;

        var generatorMarker = provenance.Any(IsStrongAiMarker);
        var provenanceOnly = provenance.Any(x =>
            x.Contains("C2PA", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("Content Credentials", StringComparison.OrdinalIgnoreCase));

        string verdict;
        double confidence;

        if (generatorMarker)
        {
            verdict = "AI generator metadata detected";
            confidence = Math.Max(0.90, 1.0 - disagreement * 0.25);
        }
        else if (isVideo &&
                 primary >= 0.78 &&
                 secondary >= 0.78 &&
                 strongAiRatio >= 0.65 &&
                 disagreement <= 0.20)
        {
            verdict = "Likely AI-generated";
            confidence = Math.Clamp(
                0.55 * Math.Min(primary, secondary) +
                0.30 * strongAiRatio +
                0.15 * (1.0 - disagreement),
                0,
                0.98);
        }
        else if (!isVideo &&
                 primary >= 0.85 &&
                 secondary >= 0.85 &&
                 disagreement <= 0.18)
        {
            verdict = "Likely AI-generated";
            confidence = Math.Clamp(
                0.75 * Math.Min(primary, secondary) +
                0.25 * (1.0 - disagreement),
                0,
                0.98);
        }
        else if (disagreement >= 0.35)
        {
            verdict = "Detector disagreement / uncertain";
            confidence = Math.Clamp(0.25 + (1.0 - disagreement) * 0.25, 0.20, 0.50);
        }
        else if ((isVideo && strongRealRatio >= 0.55 && primary <= 0.45 && secondary <= 0.45) ||
                 (!isVideo && primary <= 0.30 && secondary <= 0.30))
        {
            verdict = "Likely conventional / camera-origin";
            confidence = Math.Clamp(
                0.55 * (1.0 - Math.Max(primary, secondary)) +
                0.30 * (isVideo ? strongRealRatio : 1.0) +
                0.15 * (1.0 - disagreement),
                0,
                0.97);
        }
        else if (isVideo &&
                 strongAiRatio < 0.35 &&
                 strongRealRatio > 0.35 &&
                 ensemble < 0.55)
        {
            verdict = "Probably conventional / insufficient AI evidence";
            confidence = Math.Clamp(
                0.45 + strongRealRatio * 0.30 - disagreement * 0.20,
                0.35,
                0.80);
        }
        else
        {
            verdict = "Uncertain";
            confidence = Math.Clamp(
                0.25 + Math.Abs(ensemble - 0.5) * 0.35 + (1.0 - disagreement) * 0.15,
                0.25,
                0.65);
        }

        if (provenanceOnly && !generatorMarker)
            confidence = Math.Min(confidence, 0.90);

        var notes =
            "Conservative local ensemble. A media item is only labelled likely AI when both independent detectors " +
            "strongly agree; video also requires consistent evidence across most sampled frames. Aspect ratio is preserved " +
            "and three spatial crops are evaluated per frame to avoid portrait/landscape distortion false positives. " +
            "C2PA presence alone is not treated as proof of AI generation. Compression, screenshots, heavy editing and new generators can still affect accuracy.";

        return new AiOriginDetectionResult(
            verdict,
            ensemble,
            confidence,
            scores.Count,
            strongAiFrames,
            provenance,
            $"{PrimaryModelName} + {SecondaryModelName} • 3-crop ensemble",
            notes,
            primary,
            secondary,
            disagreement);
    }

    private static bool IsStrongAiMarker(string marker)
        => marker.StartsWith("Generator metadata:", StringComparison.OrdinalIgnoreCase);

    private async Task<FrameScore> ClassifyEnsembleAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            var tensors = CreateImageTensors(path);
            var primaryScores = new List<double>(tensors.Count);
            var secondaryScores = new List<double>(tensors.Count);

            foreach (var tensor in tensors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                primaryScores.Add(RunClassifier(GetPrimarySession(), tensor, fakeIndex: 1));
                secondaryScores.Add(RunClassifier(GetSecondarySession(), tensor, fakeIndex: 0));
            }

            return new FrameScore(
                Median(primaryScores),
                Median(secondaryScores));
        }, cancellationToken);
    }

    private static double RunClassifier(
        InferenceSession session,
        DenseTensor<float> tensor,
        int fakeIndex)
    {
        var inputName = session.InputMetadata.Keys.First();
        var input = NamedOnnxValue.CreateFromTensor(inputName, tensor);
        using var results = session.Run([input]);
        var output = results.First().AsEnumerable<float>().ToArray();

        if (output.Length < 2)
            throw new InvalidDataException("AI detector returned an unexpected output tensor.");

        var max = output.Max();
        var exps = output.Select(x => Math.Exp(x - max)).ToArray();
        var sum = exps.Sum();

        return sum <= 0
            ? 0.5
            : exps[Math.Clamp(fakeIndex, 0, exps.Length - 1)] / sum;
    }

    private InferenceSession GetPrimarySession()
        => _primarySession ??= CreateSession(PrimaryModelPath);

    private InferenceSession GetSecondarySession()
        => _secondarySession ??= CreateSession(SecondaryModelPath);

    private static InferenceSession CreateSession(string path)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
            InterOpNumThreads = 1
        };

        return new InferenceSession(path, options);
    }

    private static List<DenseTensor<float>> CreateImageTensors(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        BitmapSource source = decoder.Frames[0];
        if (source.Format != PixelFormats.Bgra32)
            source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        const int size = 224;
        var scale = Math.Max(
            (double)size / Math.Max(1, source.PixelWidth),
            (double)size / Math.Max(1, source.PixelHeight));

        var resized = new TransformedBitmap(
            source,
            new ScaleTransform(scale, scale));

        var width = Math.Max(size, resized.PixelWidth);
        var height = Math.Max(size, resized.PixelHeight);

        // Three positions along the long dimension: protects portrait/landscape media
        // from the severe 224x224 aspect-ratio distortion used by the old detector.
        double[] positions = [0.15, 0.50, 0.85];
        var tensors = new List<DenseTensor<float>>(positions.Length);

        foreach (var position in positions)
        {
            var maxX = Math.Max(0, width - size);
            var maxY = Math.Max(0, height - size);

            var x = width > height
                ? (int)Math.Round(maxX * position)
                : maxX / 2;
            var y = height > width
                ? (int)Math.Round(maxY * position)
                : maxY / 2;

            x = Math.Clamp(x, 0, maxX);
            y = Math.Clamp(y, 0, maxY);

            var crop = new CroppedBitmap(
                resized,
                new Int32Rect(x, y, size, size));

            tensors.Add(ToTensor(crop));
        }

        return tensors;
    }

    private static DenseTensor<float> ToTensor(BitmapSource source)
    {
        const int width = 224;
        const int height = 224;

        BitmapSource converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

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
        const int maxBytes = 12 * 1024 * 1024;
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
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                found.Add($"Generator metadata: {marker}");
        }

        if (ProvenanceMarkers.Any(marker =>
                text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            found.Add("C2PA / Content Credentials marker present (not proof of AI by itself)");
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

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0.5;

        var sorted = values.OrderBy(x => x).ToArray();
        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
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
        _primarySession?.Dispose();
        _secondarySession?.Dispose();
        _http.Dispose();
    }

    private sealed record FrameScore(double PrimaryAi, double SecondaryAi);
    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
