using System.Text.Json;

namespace DlssNrManager.Services;

public enum AiStudioTaskKind
{
    TextToImage,
    ImageToImage,
    InpaintOutpaint,
    TextToVideo,
    ImageToVideo,
    VideoToVideo
}

public enum AiStudioBackend
{
    Auto,
    ComfyUi,
    Diffusers
}

public enum AiStudioModelTier
{
    Recommended,
    MaximumQuality,
    Compatibility
}

public sealed record AiStudioTaskChoice(
    AiStudioTaskKind Task,
    string Label,
    string Description);

public sealed record AiStudioModelDescriptor(
    string Id,
    string DisplayName,
    string Family,
    AiStudioModelTier Tier,
    IReadOnlyList<AiStudioTaskKind> Tasks,
    IReadOnlyList<AiStudioBackend> Backends,
    string Repository,
    string License,
    bool ManagerOwnedRedistributionAllowed,
    string QualityLabel,
    string HardwareLabel,
    string Notes)
{
    public override string ToString()
        => $"{DisplayName} — {QualityLabel}";
}

public sealed record AiStudioJob(
    Guid Id,
    DateTimeOffset CreatedAt,
    AiStudioTaskKind Task,
    string ModelId,
    AiStudioBackend Backend,
    string Prompt,
    string? InputPath,
    string? MaskPath,
    string OutputFolder,
    string Status);

public sealed class LocalAiStudioService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    public static IReadOnlyList<AiStudioTaskChoice> TaskChoices { get; } =
    [
        new(
            AiStudioTaskKind.TextToImage,
            "Text-to-Image",
            "Generate a new image from a text prompt."),
        new(
            AiStudioTaskKind.ImageToImage,
            "Image-to-Image",
            "Edit or restyle a source image while preserving composition."),
        new(
            AiStudioTaskKind.InpaintOutpaint,
            "Inpainting / Outpainting",
            "Repair, replace or extend selected image regions."),
        new(
            AiStudioTaskKind.TextToVideo,
            "Text-to-Video",
            "Generate a video clip from a text prompt."),
        new(
            AiStudioTaskKind.ImageToVideo,
            "Image-to-Video",
            "Animate a still image while preserving identity and composition."),
        new(
            AiStudioTaskKind.VideoToVideo,
            "Video-to-Video",
            "Transform an existing video while preserving temporal structure.")
    ];

    public static IReadOnlyList<AiStudioModelDescriptor> Models { get; } =
    [
        new(
            "flux2-klein-4b",
            "FLUX.2 [klein] 4B",
            "FLUX.2",
            AiStudioModelTier.Recommended,
            [
                AiStudioTaskKind.TextToImage,
                AiStudioTaskKind.ImageToImage,
                AiStudioTaskKind.InpaintOutpaint
            ],
            [ AiStudioBackend.ComfyUi, AiStudioBackend.Diffusers ],
            "black-forest-labs/FLUX.2-klein-4B",
            "Apache-2.0",
            true,
            "Recommended • high quality / reliable",
            "Consumer NVIDIA GPU • ~13 GB VRAM class",
            "Primary image model. Unified text generation and image editing; preferred default for local production."),

        new(
            "flux2-dev",
            "FLUX.2 [dev]",
            "FLUX.2",
            AiStudioModelTier.MaximumQuality,
            [
                AiStudioTaskKind.TextToImage,
                AiStudioTaskKind.ImageToImage,
                AiStudioTaskKind.InpaintOutpaint
            ],
            [ AiStudioBackend.ComfyUi, AiStudioBackend.Diffusers ],
            "black-forest-labs/FLUX.2-dev",
            "FLUX [dev] Non-Commercial License v2.0",
            false,
            "Maximum FLUX image quality",
            "Very high VRAM / 32B model / quantized local paths recommended",
            "Quality-first FLUX option. Gated model; manual license acceptance is required and the model itself is restricted to non-commercial/non-production use."),

        new(
            "qwen-image-2.1",
            "Qwen-Image-2.1",
            "Qwen Image",
            AiStudioModelTier.MaximumQuality,
            [
                AiStudioTaskKind.TextToImage,
                AiStudioTaskKind.ImageToImage,
                AiStudioTaskKind.InpaintOutpaint
            ],
            [ AiStudioBackend.ComfyUi, AiStudioBackend.Diffusers ],
            "Qwen/Qwen-Image-2.1",
            "Qwen Research License",
            false,
            "Maximum image quality / editing",
            "High VRAM / large model",
            "Premium image option. Manual installation/acceptance only because the model weights use the Qwen Research License."),

        new(
            "sdxl-1.0",
            "Stable Diffusion XL 1.0",
            "SDXL",
            AiStudioModelTier.Compatibility,
            [
                AiStudioTaskKind.TextToImage,
                AiStudioTaskKind.ImageToImage
            ],
            [ AiStudioBackend.ComfyUi, AiStudioBackend.Diffusers ],
            "stabilityai/stable-diffusion-xl-base-1.0",
            "CreativeML Open RAIL++-M",
            true,
            "Mature / highly compatible",
            "Moderate VRAM",
            "Compatibility fallback with a large ecosystem of workflows and control tools."),

        new(
            "sdxl-inpaint-1.0",
            "SDXL Inpainting 1.0",
            "SDXL",
            AiStudioModelTier.Compatibility,
            [ AiStudioTaskKind.InpaintOutpaint ],
            [ AiStudioBackend.ComfyUi, AiStudioBackend.Diffusers ],
            "diffusers/stable-diffusion-xl-1.0-inpainting-0.1",
            "CreativeML Open RAIL++-M",
            true,
            "Reliable dedicated inpainting",
            "Moderate VRAM",
            "Dedicated mask-based inpainting model; preferred compatibility fallback for precise masked repairs."),

        new(
            "wan2.2-ti2v-5b",
            "Wan2.2 TI2V 5B",
            "Wan2.2",
            AiStudioModelTier.Recommended,
            [
                AiStudioTaskKind.TextToVideo,
                AiStudioTaskKind.ImageToVideo
            ],
            [ AiStudioBackend.ComfyUi, AiStudioBackend.Diffusers ],
            "Wan-AI/Wan2.2-TI2V-5B",
            "Apache-2.0",
            true,
            "Recommended local video",
            "Large GPU workload • optimized local profile",
            "Primary practical local video model for text/image-to-video."),

        new(
            "wan2.2-t2v-a14b",
            "Wan2.2 T2V A14B",
            "Wan2.2",
            AiStudioModelTier.MaximumQuality,
            [ AiStudioTaskKind.TextToVideo ],
            [ AiStudioBackend.ComfyUi, AiStudioBackend.Diffusers ],
            "Wan-AI/Wan2.2-T2V-A14B",
            "Apache-2.0",
            true,
            "Maximum text-to-video quality",
            "Very large model / high VRAM + disk",
            "Quality-first text-to-video option. Keep optional because the full checkpoint is very large."),

        new(
            "wan2.2-i2v-a14b",
            "Wan2.2 I2V A14B",
            "Wan2.2",
            AiStudioModelTier.MaximumQuality,
            [ AiStudioTaskKind.ImageToVideo ],
            [ AiStudioBackend.ComfyUi, AiStudioBackend.Diffusers ],
            "Wan-AI/Wan2.2-I2V-A14B",
            "Apache-2.0",
            true,
            "Maximum image-to-video quality",
            "Very large model / high VRAM + disk",
            "Quality-first image-to-video option with stronger identity/detail preservation than the practical 5B profile."),

        new(
            "wan2.2-animate-14b",
            "Wan2.2 Animate 14B",
            "Wan2.2",
            AiStudioModelTier.MaximumQuality,
            [ AiStudioTaskKind.VideoToVideo ],
            [ AiStudioBackend.ComfyUi, AiStudioBackend.Diffusers ],
            "Wan-AI/Wan2.2-Animate-14B",
            "Apache-2.0",
            true,
            "High-fidelity video transformation",
            "Very large model / high VRAM + disk",
            "Advanced video-to-video/character animation model; optional due to size."),

        new(
            "ltx-2.5",
            "LTX-2.5 Pre-Trained",
            "LTX-2.x",
            AiStudioModelTier.MaximumQuality,
            [
                AiStudioTaskKind.TextToVideo,
                AiStudioTaskKind.ImageToVideo,
                AiStudioTaskKind.VideoToVideo
            ],
            [ AiStudioBackend.ComfyUi, AiStudioBackend.Diffusers ],
            "Lightricks/LTX-2.5-Pre-Trained",
            "LTX-2.x Community License",
            false,
            "Advanced production / audio-video",
            "High VRAM / large model",
            "Optional advanced engine. Manual license acceptance required before model weights can be installed.")
    ];

    public string Root { get; } =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager",
            "ai-studio");

    public string ModelsRoot => Path.Combine(Root, "models");
    public string OutputsRoot => Path.Combine(Root, "outputs");
    public string JobsRoot => Path.Combine(Root, "jobs");
    public string WorkflowsRoot => Path.Combine(Root, "workflows");
    public string RuntimeRoot => Path.Combine(Root, "runtime");

    public IReadOnlyList<AiStudioModelDescriptor> GetModels(
        AiStudioTaskKind task)
        => Models
            .Where(x => x.Tasks.Contains(task))
            .OrderBy(x => x.Tier)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void EnsureWorkspace()
    {
        foreach (var path in new[]
                 {
                     Root,
                     ModelsRoot,
                     OutputsRoot,
                     JobsRoot,
                     WorkflowsRoot,
                     RuntimeRoot,
                     Path.Combine(RuntimeRoot, "python"),
                     Path.Combine(RuntimeRoot, "comfyui"),
                     Path.Combine(RuntimeRoot, "diffusers"),
                     Path.Combine(RuntimeRoot, "wheelhouse")
                 })
        {
            Directory.CreateDirectory(path);
        }

        var policy = new
        {
            schema = 1,
            isolation = new
            {
                python_no_user_site = true,
                pip_no_index_in_offline_mode = true,
                manager_owned_runtime_only = true,
                global_python_mutation = false,
                global_cuda_toolkit_mutation = false
            },
            roots = new
            {
                runtime = RuntimeRoot,
                models = ModelsRoot,
                outputs = OutputsRoot,
                jobs = JobsRoot,
                workflows = WorkflowsRoot
            }
        };

        File.WriteAllText(
            Path.Combine(Root, "runtime-policy.json"),
            JsonSerializer.Serialize(policy, JsonOptions));
    }

    public string GetModelDirectory(
        AiStudioModelDescriptor model)
        => Path.Combine(
            ModelsRoot,
            Sanitize(model.Id));

    public bool IsModelInstalled(AiStudioModelDescriptor model)
        => Directory.Exists(
            GetModelDirectory(model));

    public void RemoveModel(
        AiStudioModelDescriptor model)
    {
        var path = GetModelDirectory(model);
        ManagedPathSafety.EnsureSafeForRemoval(path);

        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    public async Task ImportModelDirectoryAsync(
        AiStudioModelDescriptor model,
        string sourceDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) ||
            !Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                "Le dossier source du modèle est introuvable.");
        }

        EnsureWorkspace();

        var sourceRoot =
            Path.GetFullPath(sourceDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        var target =
            Path.GetFullPath(
                GetModelDirectory(model));

        if (sourceRoot.Equals(
                target.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var files =
            Directory
                .EnumerateFiles(
                    sourceRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Select(path =>
                    new FileInfo(path))
                .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidDataException(
                "Le dossier sélectionné ne contient aucun fichier de modèle.");
        }

        long totalBytes = 0;
        foreach (var file in files)
        {
            checked
            {
                totalBytes += file.Length;
            }
        }

        var staging =
            target +
            ".staging-" +
            Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(staging);

        using var transfer =
            DownloadProgressHub.Begin(
                $"Import • {model.DisplayName}",
                totalBytes);

        long copied = 0;

        try
        {
            progress?.Report(
                $"Import local de {model.DisplayName}…");

            var buffer =
                new byte[1024 * 1024];

            foreach (var file in files)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var relative =
                    Path.GetRelativePath(
                        sourceRoot,
                        file.FullName);

                var destination =
                    Path.Combine(
                        staging,
                        relative);

                Directory.CreateDirectory(
                    Path.GetDirectoryName(
                        destination)!);

                await using var input =
                    new FileStream(
                        file.FullName,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        1024 * 1024,
                        useAsync: true);

                await using var output =
                    new FileStream(
                        destination,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024,
                        useAsync: true);

                while (true)
                {
                    var read =
                        await input.ReadAsync(
                            buffer.AsMemory(
                                0,
                                buffer.Length),
                            cancellationToken);

                    if (read == 0)
                        break;

                    await output.WriteAsync(
                        buffer.AsMemory(
                            0,
                            read),
                        cancellationToken);

                    copied += read;
                    transfer.Report(copied);
                }
            }

            // Copy/verify all source files before touching the installed
            // model. Swapping is guarded by a real backup and rollback.
            ManualModelDirectorySwap.ReplaceStaged(staging, target);

            transfer.Complete();

            progress?.Report(
                $"{model.DisplayName} importé dans le stockage AI Studio.");
        }
        catch (Exception ex)
        {
            transfer.Fail(ex);

            if (Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(
                        staging,
                        true);
                }
                catch { }
            }

            throw;
        }
    }

    public string GetRuntimeSummary(
        string language = "en")
    {
        var python = File.Exists(
            Path.Combine(RuntimeRoot, "python", "python.exe"));
        var comfy = File.Exists(
            Path.Combine(RuntimeRoot, "comfyui", "main.py"));
        var diffusers = Directory.Exists(
            Path.Combine(RuntimeRoot, "diffusers", "src", "diffusers"));

        string T(string english, string french)
            => UiLocalizationService.NormalizeLanguage(language) == "fr"
                ? french
                : english;

        // File presence is not proof of an approved, hash-verified runtime or
        // of a reviewed, licensed inference executor. Keep this status read-only.
        string FileStatus(bool found)
            => found
                ? T("detected (unverified)", "détecté (non vérifié)")
                : T("not found", "absent");

        return
            $"{T("Workspace", "Espace de travail")}: {(Directory.Exists(Root) ? T("prepared", "préparé") : T("not prepared", "non préparé"))} • " +
            $"Python: {FileStatus(python)} • " +
            $"ComfyUI: {FileStatus(comfy)} • " +
            $"Diffusers: {FileStatus(diffusers)} • " +
            T("Execution unavailable until the runtime, model licenses and executor are verified.",
              "Exécution indisponible tant que le runtime, les licences des modèles et le moteur d’exécution ne sont pas vérifiés.");
    }

    public string GetModelStatus(
        AiStudioModelDescriptor model,
        string language = "en")
    {
        var french =
            UiLocalizationService.NormalizeLanguage(language) == "fr";

        // Directory presence only means local files were found; it does not
        // authenticate weights or authorize a model/runtime for inference.
        var localFiles = IsModelInstalled(model)
            ? french ? "Fichiers détectés (non vérifiés)" : "Files detected (unverified)"
            : french ? "Fichiers absents" : "Files not found";

        var distribution = model.ManagerOwnedRedistributionAllowed
            ? french
                ? "éligible manager-owned/offline"
                : "manager-owned/offline eligible"
            : french
                ? "acceptation manuelle de la licence requise"
                : "manual license acceptance required";

        return $"{localFiles} • {model.License} • {distribution}";
    }

    public AiStudioJob QueueJob(
        AiStudioTaskKind task,
        AiStudioModelDescriptor model,
        AiStudioBackend backend,
        string prompt,
        string? inputPath,
        string? maskPath,
        string outputFolder)
    {
        EnsureWorkspace();
        Directory.CreateDirectory(outputFolder);

        var job = new AiStudioJob(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            task,
            model.Id,
            backend,
            prompt,
            inputPath,
            maskPath,
            outputFolder,
            "Queued");

        File.WriteAllText(
            Path.Combine(JobsRoot, $"{job.Id:N}.json"),
            JsonSerializer.Serialize(job, JsonOptions));

        return job;
    }

    public IReadOnlyList<AiStudioJob> LoadJobs()
    {
        EnsureWorkspace();

        var jobs = new List<AiStudioJob>();
        foreach (var path in Directory.EnumerateFiles(
                     JobsRoot,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                var job = JsonSerializer.Deserialize<AiStudioJob>(
                    File.ReadAllText(path));
                if (job != null)
                    jobs.Add(job);
            }
            catch
            {
                // Corrupt/incomplete job manifests are ignored but preserved.
            }
        }

        return jobs
            .OrderByDescending(x => x.CreatedAt)
            .ToArray();
    }

    private static string Sanitize(string value)
        => string.Concat(
            value.Select(ch =>
                char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'
                    ? ch
                    : '_'));
}
