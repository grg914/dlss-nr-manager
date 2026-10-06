using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DlssNrManager.Services;

public enum AiOriginAnalysisMode
{
    Quick,
    Balanced,
    Thorough
}

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
    double ModelDisagreement = 0,
    double ViewConsistency = 1,
    double TemporalConsistency = 1,
    string AnalysisMode = "Balanced")
{
    public string Summary =>
        $"{Verdict} • ensemble AI score {AiProbability:P0} • confidence {Confidence:P0}" +
        (FramesAnalyzed > 1 ? $" • strong-AI frames {FramesFlagged}/{FramesAnalyzed}" : "");
}

public sealed class AiOriginDetectionService : IDisposable
{
    private const long MaxModelDownloadBytes = 256L * 1024 * 1024;
    private const string PrimaryModelName = "CapCheck ViT AI-vs-Real";
    private const string PrimaryModelUrl =
        "https://github.com/grg914/dlss-nr-manager/releases/latest/download/ai-origin-primary-int8.onnx";
    private const string PrimaryModelSha256 =
        "08B349F1B535F2F0CC2A8610BBF57C27593A0364E78B6C91205C0FF2BF29D714";

    private const string SecondaryModelName = "AI Image Detect Distilled ViT";
    private const string SecondaryModelUrl =
        "https://github.com/grg914/dlss-nr-manager/releases/latest/download/ai-origin-secondary-int8.onnx";
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
    private readonly SemaphoreSlim _setupLock = new(1, 1);
    private InferenceSession? _primarySession;
    private InferenceSession? _secondarySession;
    private bool _modelsVerified;

    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "ai-origin-detector");

    public string PrimaryModelPath => Path.Combine(RootDirectory, "ai-image-detector-int8.onnx");
    public string SecondaryModelPath => Path.Combine(RootDirectory, "ai-image-detector-distilled-int8.onnx");

    public bool IsInstalled =>
        File.Exists(PrimaryModelPath) &&
        File.Exists(SecondaryModelPath);

    public bool IsReady => _modelsVerified;

    public AiOriginDetectionService(MediaService media)
    {
        _media = media;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"DlssNrManager/{AppIdentity.UserAgentVersion}");
    }

    public async Task SetupAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool forceVerify = false)
    {
        await _setupLock.WaitAsync(cancellationToken);
        try
        {
            if (_modelsVerified && !forceVerify)
            {
                progress?.Report("AI origin detector ready.");
                return;
            }

            if (forceVerify)
            {
                _primarySession?.Dispose();
                _primarySession = null;
                _secondarySession?.Dispose();
                _secondarySession = null;
                _modelsVerified = false;
            }

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

            _modelsVerified = true;
            progress?.Report("AI origin detector ready • 2-model local ensemble verified.");
        }
        finally
        {
            _setupLock.Release();
        }
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

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(
                "github.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unexpected manager-owned AI detector model URL: {url}");
        }

        var temp = destination + ".download";

        try
        {
            await NetworkRetry.ExecuteAsync(
                async (attempt, token) =>
                {
                    if (attempt > 1)
                    {
                        TryDeleteFile(temp);
                        progress?.Report(
                            $"Retrying {label} download ({attempt}/3)…");
                    }

                    using var response = await _http.GetAsync(
                        uri,
                        HttpCompletionOption.ResponseHeadersRead,
                        token);
                    response.EnsureSuccessStatusCode();

                    if (response.Content.Headers.ContentLength is > MaxModelDownloadBytes)
                    {
                        throw new InvalidDataException(
                            "AI detector model exceeds the 256 MB safety limit.");
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
                            MaxModelDownloadBytes,
                            token);
                    }

                    var actual = await Sha256Async(temp, token);
                    if (!actual.Equals(
                            expectedSha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"AI detector SHA-256 mismatch. Expected {expectedSha256}, got {actual}.");
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
                    $"Download exceeded the {maxBytes / (1024 * 1024)} MB safety limit.");
            }

            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }
    }

    public async Task<AiOriginDetectionResult> AnalyzeAsync(
        string source,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        AiOriginAnalysisMode mode = AiOriginAnalysisMode.Balanced)
    {
        if (!File.Exists(source))
            throw new FileNotFoundException("Media file was not found.", source);

        if (!_modelsVerified)
            await SetupAsync(progress, cancellationToken);

        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" or ".webp")
            return await AnalyzeImageAsync(source, progress, cancellationToken, mode);

        if (extension is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".m4v")
            return await AnalyzeVideoAsync(source, progress, cancellationToken, mode);

        throw new NotSupportedException(
            $"Unsupported AI-origin media type: {extension}");
    }

    private async Task<AiOriginDetectionResult> AnalyzeImageAsync(
        string source,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        AiOriginAnalysisMode mode)
    {
        progress?.Report("Analyzing provenance + two independent visual detectors…");

        var provenance = await FindImageProvenanceSignalsAsync(source, cancellationToken);
        var score = await ClassifyEnsembleAsync(source, cancellationToken, mode);

        return BuildResult([score], provenance, isVideo: false, mode);
    }

    private async Task<AiOriginDetectionResult> AnalyzeVideoAsync(
        string source,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        AiOriginAnalysisMode mode)
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
            var targetFrames = mode switch
            {
                AiOriginAnalysisMode.Quick => 8,
                AiOriginAnalysisMode.Thorough => 24,
                _ => 20
            };

            var fps = duration > 0
                ? Math.Clamp(targetFrames / duration, 0.02, 6.0)
                : 1.0;

            progress?.Report(
                $"Sampling up to {targetFrames} uniform frames across the full video • {mode} mode…");

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
                    Path.Combine(session, "uniform-%04d.png")
                ],
                Path.GetDirectoryName(_media.FfmpegPath)!,
                cancellationToken);

            if (extraction.ExitCode != 0)
                throw new InvalidOperationException(
                    "FFmpeg video sampling failed.\n" + Tail(extraction.Error, 2200));

            if (mode == AiOriginAnalysisMode.Thorough)
            {
                progress?.Report(
                    "Adding scene-change frames for better temporal coverage…");

                var sceneExtraction = await RunAsync(
                    _media.FfmpegPath,
                    [
                        "-y",
                        "-hide_banner",
                        "-loglevel", "error",
                        "-i", source,
                        "-vf", "select=gt(scene\\,0.30)",
                        "-frames:v", "12",
                        "-fps_mode", "vfr",
                        Path.Combine(session, "scene-%04d.png")
                    ],
                    Path.GetDirectoryName(_media.FfmpegPath)!,
                    cancellationToken);

                if (sceneExtraction.ExitCode != 0)
                {
                    AppLogger.Warn(
                        "Scene-aware AI video sampling failed; continuing with uniform frames only.");
                }
            }

            var frames = Directory
                .EnumerateFiles(session, "*.png")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (frames.Count == 0)
                throw new InvalidOperationException("No frames were extracted for AI-origin analysis.");

            _ = GetPrimarySession();
            _ = GetSecondarySession();

            var scores = new FrameScore[frames.Count];
            var completed = 0;
            var parallelism = Math.Clamp(
                Environment.ProcessorCount / 6,
                1,
                2);

            await Parallel.ForEachAsync(
                Enumerable.Range(0, frames.Count),
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = parallelism
                },
                async (index, token) =>
                {
                    scores[index] = await ClassifyEnsembleAsync(
                        frames[index],
                        token,
                        mode);

                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(
                        $"AI ensemble analysis • frame {done}/{frames.Count}");
                });

            return BuildResult(scores, provenance, isVideo: true, mode);
        }
        finally
        {
            TryDeleteDirectory(session);
        }
    }

    private AiOriginDetectionResult BuildResult(
        IReadOnlyList<FrameScore> scores,
        IReadOnlyList<string> provenance,
        bool isVideo,
        AiOriginAnalysisMode mode)
    {
        if (scores.Count == 0)
            throw new InvalidOperationException("AI detector produced no scores.");

        var primary = Median(scores.Select(x => x.PrimaryAi).ToList());
        var secondary = Median(scores.Select(x => x.SecondaryAi).ToList());
        var disagreement = Math.Abs(primary - secondary);

        var viewSpread = Median(
            scores.Select(x =>
                Math.Max(
                    x.PrimaryViewSpread,
                    x.SecondaryViewSpread))
                .ToList());

        var viewConsistency = Math.Clamp(
            1.0 - viewSpread / 0.35,
            0,
            1);

        var frameEnsembles = scores
            .Select(x => Math.Sqrt(
                Math.Clamp(x.PrimaryAi, 0, 1) *
                Math.Clamp(x.SecondaryAi, 0, 1)))
            .ToList();

        var temporalConsistency = isVideo
            ? Math.Clamp(
                1.0 - StandardDeviation(frameEnsembles) / 0.25,
                0,
                1)
            : 1.0;

        var shortEdge = Median(
            scores.Select(x => (double)x.InputShortEdge).ToList());

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

        var visualStrongAi =
            primary >= 0.72 &&
            secondary >= 0.72 &&
            disagreement <= 0.25;

        var visualStrongReal =
            primary <= 0.30 &&
            secondary <= 0.30;

        if (generatorMarker && visualStrongAi)
        {
            verdict = "Likely AI-generated (metadata + visual evidence)";
            confidence = Math.Clamp(
                0.70 * Math.Min(primary, secondary) +
                0.15 * viewConsistency +
                0.15 * (1.0 - disagreement),
                0.72,
                0.95);
        }
        else if (generatorMarker && visualStrongReal)
        {
            verdict = "Generator metadata present / visual evidence conflicts";
            confidence = 0.40;
        }
        else if (generatorMarker)
        {
            verdict = "Generator metadata present / origin uncertain";
            confidence = Math.Clamp(
                0.45 + 0.15 * ensemble,
                0.45,
                0.62);
        }
        else if (isVideo &&
                 primary >= 0.78 &&
                 secondary >= 0.78 &&
                 strongAiRatio >= 0.65 &&
                 disagreement <= 0.20 &&
                 viewConsistency >= 0.60 &&
                 temporalConsistency >= 0.55)
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
                 disagreement <= 0.18 &&
                 viewConsistency >= 0.60)
        {
            verdict = "Likely AI-generated";
            confidence = Math.Clamp(
                0.75 * Math.Min(primary, secondary) +
                0.25 * (1.0 - disagreement),
                0,
                0.98);
        }
        else if (disagreement >= 0.35 ||
                 viewConsistency < 0.52 ||
                 (isVideo && temporalConsistency < 0.42))
        {
            verdict = "Detector disagreement / uncertain";
            confidence = Math.Clamp(
                0.20 +
                (1.0 - disagreement) * 0.18 +
                viewConsistency * 0.08 +
                temporalConsistency * 0.08,
                0.20,
                0.52);
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

        confidence *= 0.78 + 0.22 * viewConsistency;
        if (isVideo)
            confidence *= 0.82 + 0.18 * temporalConsistency;

        if (shortEdge < 256)
            confidence = Math.Min(confidence, 0.68);
        if (shortEdge < 128)
            confidence = Math.Min(confidence, 0.52);

        confidence = Math.Clamp(confidence, 0, 0.98);

        var notes =
            "Conservative local ensemble. A media item is only labelled likely AI when both independent detectors " +
            "strongly agree. Confidence is reduced when spatial views disagree, video frames are temporally inconsistent, " +
            "or the source resolution is too low for reliable forensic cues. Quick mode uses a model-native full-frame view, " +
            "Balanced adds three aspect-preserving crops, and Thorough adds a second higher-resolution crop scale plus scene-aware " +
            "video sampling. Structured generator metadata is supporting evidence only and cannot by itself prove AI origin. " +
            "distinguished from unverified raw markers. C2PA presence alone is not treated as proof of AI generation. " +
            "Compression, screenshots, heavy editing and new generators can still affect accuracy.";

        return new AiOriginDetectionResult(
            verdict,
            ensemble,
            confidence,
            scores.Count,
            strongAiFrames,
            provenance,
            $"{PrimaryModelName} + {SecondaryModelName} • " +
            (mode switch
            {
                AiOriginAnalysisMode.Quick => "model-native full-frame ensemble",
                AiOriginAnalysisMode.Thorough => "full-frame + multi-scale crop ensemble",
                _ => "full-frame + 3-crop ensemble"
            }),
            notes,
            primary,
            secondary,
            disagreement,
            viewConsistency,
            temporalConsistency,
            mode.ToString());
    }

    private static bool IsStrongAiMarker(string marker)
        => marker.StartsWith("Generator metadata:", StringComparison.OrdinalIgnoreCase);

    private async Task<FrameScore> ClassifyEnsembleAsync(
        string path,
        CancellationToken cancellationToken,
        AiOriginAnalysisMode mode)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            var views = CreateImageTensors(path, mode);
            var primaryScores = new List<double>(views.Tensors.Count);
            var secondaryScores = new List<double>(views.Tensors.Count);

            foreach (var tensor in views.Tensors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                primaryScores.Add(
                    RunClassifier(
                        GetPrimarySession(),
                        tensor,
                        fakeIndex: 1));
                secondaryScores.Add(
                    RunClassifier(
                        GetSecondarySession(),
                        tensor,
                        fakeIndex: 0));
            }

            return new FrameScore(
                Median(primaryScores),
                Median(secondaryScores),
                StandardDeviation(primaryScores),
                StandardDeviation(secondaryScores),
                views.ShortEdge);
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
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 4, 1, 6),
            InterOpNumThreads = 1
        };

        return new InferenceSession(path, options);
    }

    private static ImageTensorSet CreateImageTensors(
        string path,
        AiOriginAnalysisMode mode)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan);

        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnDemand);

        BitmapSource source = decoder.Frames[0];
        if (source.Format != PixelFormats.Bgra32)
        {
            source = new FormatConvertedBitmap(
                source,
                PixelFormats.Bgra32,
                null,
                0);
        }

        const int size = 224;
        const long maxPixels = 120_000_000;

        var pixels =
            (long)Math.Max(1, source.PixelWidth) *
            Math.Max(1, source.PixelHeight);

        if (pixels > maxPixels)
        {
            throw new InvalidDataException(
                $"Image dimensions are too large for safe local analysis ({source.PixelWidth}x{source.PixelHeight}).");
        }

        var shortEdge = Math.Max(
            1,
            Math.Min(source.PixelWidth, source.PixelHeight));

        var tensors = new List<DenseTensor<float>>();

        // Include the model-native 224x224 resize used by the original
        // Hugging Face image processors so the ensemble stays calibrated
        // to the models' training/inference convention.
        tensors.Add(ToTensor(ResizeExact(source, size, size)));

        if (mode == AiOriginAnalysisMode.Quick)
        {
            return new ImageTensorSet(
                tensors,
                shortEdge);
        }

        AddAspectPreservingCrops(
            source,
            size,
            size,
            [0.15, 0.50, 0.85],
            tensors);

        if (mode == AiOriginAnalysisMode.Thorough &&
            shortEdge >= 384)
        {
            AddAspectPreservingCrops(
                source,
                448,
                size,
                [0.15, 0.50, 0.85],
                tensors);
        }

        return new ImageTensorSet(
            tensors,
            shortEdge);
    }

    private static BitmapSource ResizeExact(
        BitmapSource source,
        int width,
        int height)
    {
        return new TransformedBitmap(
            source,
            new ScaleTransform(
                (double)width / Math.Max(1, source.PixelWidth),
                (double)height / Math.Max(1, source.PixelHeight)));
    }

    private static void AddAspectPreservingCrops(
        BitmapSource source,
        int targetShortEdge,
        int cropSize,
        IReadOnlyList<double> positions,
        ICollection<DenseTensor<float>> tensors)
    {
        var scale = Math.Max(
            (double)targetShortEdge /
            Math.Max(1, source.PixelWidth),
            (double)targetShortEdge /
            Math.Max(1, source.PixelHeight));

        var resized = new TransformedBitmap(
            source,
            new ScaleTransform(scale, scale));

        var width = Math.Max(cropSize, resized.PixelWidth);
        var height = Math.Max(cropSize, resized.PixelHeight);

        foreach (var position in positions)
        {
            var maxX = Math.Max(0, width - cropSize);
            var maxY = Math.Max(0, height - cropSize);

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
                new Int32Rect(
                    x,
                    y,
                    cropSize,
                    cropSize));

            tensors.Add(ToTensor(crop));
        }
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
        var found = new List<string>();

        try
        {
            using var stream = File.OpenRead(source);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.FirstOrDefault()?.Metadata is BitmapMetadata metadata)
            {
                var structured = string.Join(
                    "\n",
                    new[]
                    {
                        metadata.ApplicationName,
                        metadata.Comment,
                        metadata.Subject,
                        metadata.Title,
                        metadata.CameraManufacturer,
                        metadata.CameraModel
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));

                found.AddRange(
                    FindMarkers(
                        structured,
                        structuredMetadata: true));
            }
        }
        catch
        {
            // Metadata parsing is advisory and must never block visual analysis.
        }

        var rawText = await ReadHeadAndTailTextAsync(
            source,
            8 * 1024 * 1024,
            cancellationToken);

        found.AddRange(
            FindMarkers(
                rawText,
                structuredMetadata: false));

        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        var found = new List<string>();

        try
        {
            using var json = JsonDocument.Parse(probe.Output);
            var metadataValues = new List<string>();

            if (json.RootElement.TryGetProperty("format", out var format) &&
                format.TryGetProperty("tags", out var formatTags))
            {
                CollectJsonStringValues(formatTags, metadataValues);
            }

            if (json.RootElement.TryGetProperty("streams", out var streams) &&
                streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (stream.TryGetProperty("tags", out var streamTags))
                        CollectJsonStringValues(streamTags, metadataValues);
                }
            }

            found.AddRange(
                FindMarkers(
                    string.Join("\n", metadataValues),
                    structuredMetadata: true));
        }
        catch
        {
            // A malformed ffprobe metadata payload does not invalidate visual analysis.
        }

        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectJsonStringValues(
        JsonElement element,
        ICollection<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectJsonStringValues(property.Value, values);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectJsonStringValues(item, values);
                break;

            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
                break;
        }
    }

    private static IReadOnlyList<string> FindMarkers(
        string text,
        bool structuredMetadata)
    {
        var found = new List<string>();

        foreach (var marker in AiMarkers)
        {
            if (!text.Contains(
                    marker,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            found.Add(
                structuredMetadata
                    ? $"Generator metadata: {marker}"
                    : $"Generator marker in file bytes: {marker} (unverified)");
        }

        if (ProvenanceMarkers.Any(marker =>
                text.Contains(
                    marker,
                    StringComparison.OrdinalIgnoreCase)))
        {
            found.Add(
                "C2PA / Content Credentials marker present (not proof of AI by itself)");
        }

        return found;
    }

    private static async Task<string> ReadHeadAndTailTextAsync(
        string path,
        int bytesPerSide,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            128 * 1024,
            useAsync: true);

        var headLength = (int)Math.Min(stream.Length, bytesPerSide);
        var head = new byte[headLength];
        var headRead = await stream.ReadAsync(
            head.AsMemory(),
            cancellationToken);

        if (stream.Length <= bytesPerSide)
            return Encoding.Latin1.GetString(head, 0, headRead);

        var tailLength = (int)Math.Min(stream.Length - headRead, bytesPerSide);
        var tail = new byte[tailLength];
        stream.Seek(-tailLength, SeekOrigin.End);
        var tailRead = await stream.ReadAsync(
            tail.AsMemory(),
            cancellationToken);

        return Encoding.Latin1.GetString(head, 0, headRead) +
               "\n" +
               Encoding.Latin1.GetString(tail, 0, tailRead);
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

    private static double StandardDeviation(
        IReadOnlyList<double> values)
    {
        if (values.Count <= 1)
            return 0;

        var mean = values.Average();
        var variance = values
            .Select(value => (value - mean) * (value - mean))
            .Average();

        return Math.Sqrt(variance);
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
        _setupLock.Dispose();
        _http.Dispose();
    }

    private sealed record ImageTensorSet(
        IReadOnlyList<DenseTensor<float>> Tensors,
        int ShortEdge);

    private sealed record FrameScore(
        double PrimaryAi,
        double SecondaryAi,
        double PrimaryViewSpread,
        double SecondaryViewSpread,
        int InputShortEdge);

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);
}
