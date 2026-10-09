namespace DlssNrManager.Services;

// This is a read-only diagnostic. File presence does not authorize a process launch.
// An execution service must separately verify pinned package hashes, provenance,
// license acceptance and process isolation before running a queued job.
public enum AiStudioJobPreflightStatus
{
    UnknownModel,
    UnsupportedTask,
    UnsupportedBackend,
    MissingInput,
    MissingMask,
    MissingOutputFolder,
    MissingModel,
    MissingPython,
    MissingComfyUi,
    MissingDiffusers,
    MissingBackend,
    AwaitingVerifiedExecutor
}

public sealed record AiStudioJobPreflightResult(AiStudioJobPreflightStatus Status)
{
    public string Describe(string language)
    {
        var french = UiLocalizationService.NormalizeLanguage(language) == "fr";
        return Status switch
        {
            AiStudioJobPreflightStatus.UnknownModel =>
                french ? "Modèle inconnu" : "Unknown model",
            AiStudioJobPreflightStatus.UnsupportedTask =>
                french ? "Tâche incompatible avec le modèle" : "Task not supported by model",
            AiStudioJobPreflightStatus.UnsupportedBackend =>
                french ? "Moteur incompatible avec le modèle" : "Backend not supported by model",
            AiStudioJobPreflightStatus.MissingInput =>
                french ? "Fichier source introuvable" : "Source file missing",
            AiStudioJobPreflightStatus.MissingMask =>
                french ? "Masque introuvable" : "Mask file missing",
            AiStudioJobPreflightStatus.MissingOutputFolder =>
                french ? "Dossier de sortie non défini" : "Output folder not specified",
            AiStudioJobPreflightStatus.MissingModel =>
                french ? "Modèle local absent" : "Local model missing",
            AiStudioJobPreflightStatus.MissingPython =>
                french ? "Python isolé absent" : "Isolated Python missing",
            AiStudioJobPreflightStatus.MissingComfyUi =>
                french ? "ComfyUI local absent" : "Local ComfyUI missing",
            AiStudioJobPreflightStatus.MissingDiffusers =>
                french ? "Diffusers local absent" : "Local Diffusers missing",
            AiStudioJobPreflightStatus.MissingBackend =>
                french ? "Aucun moteur local détecté" : "No local backend detected",
            AiStudioJobPreflightStatus.AwaitingVerifiedExecutor =>
                french
                    ? "Composants présents ; exécution non disponible avant validation du runtime"
                    : "Components found; execution unavailable until runtime verification",
            _ => throw new ArgumentOutOfRangeException(nameof(Status))
        };
    }
}

public sealed class AiStudioJobPreflight
{
    private readonly string _workspaceRoot;

    public AiStudioJobPreflight(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public AiStudioJobPreflightResult Inspect(AiStudioJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        AiStudioJobPreflightResult Result(AiStudioJobPreflightStatus status)
            => new(status);

        var model = LocalAiStudioService.Models.FirstOrDefault(
            candidate => string.Equals(candidate.Id, job.ModelId, StringComparison.Ordinal));

        if (model is null)
            return Result(AiStudioJobPreflightStatus.UnknownModel);

        if (!model.Tasks.Contains(job.Task))
            return Result(AiStudioJobPreflightStatus.UnsupportedTask);

        if (job.Backend != AiStudioBackend.Auto &&
            !model.Backends.Contains(job.Backend))
            return Result(AiStudioJobPreflightStatus.UnsupportedBackend);

        var needsInput = job.Task is
            AiStudioTaskKind.ImageToImage or
            AiStudioTaskKind.InpaintOutpaint or
            AiStudioTaskKind.ImageToVideo or
            AiStudioTaskKind.VideoToVideo;

        if (needsInput &&
            (string.IsNullOrWhiteSpace(job.InputPath) || !File.Exists(job.InputPath)))
            return Result(AiStudioJobPreflightStatus.MissingInput);

        if (!string.IsNullOrWhiteSpace(job.MaskPath) && !File.Exists(job.MaskPath))
            return Result(AiStudioJobPreflightStatus.MissingMask);

        if (string.IsNullOrWhiteSpace(job.OutputFolder))
            return Result(AiStudioJobPreflightStatus.MissingOutputFolder);

        if (!Directory.Exists(Path.Combine(_workspaceRoot, "models", model.Id)))
            return Result(AiStudioJobPreflightStatus.MissingModel);

        var runtime = Path.Combine(_workspaceRoot, "runtime");
        if (!File.Exists(Path.Combine(runtime, "python", "python.exe")))
            return Result(AiStudioJobPreflightStatus.MissingPython);

        var comfyFound = File.Exists(Path.Combine(runtime, "comfyui", "main.py"));
        var diffusersFound =
            Directory.Exists(Path.Combine(runtime, "diffusers", "src", "diffusers"));

        if (job.Backend == AiStudioBackend.ComfyUi && !comfyFound)
            return Result(AiStudioJobPreflightStatus.MissingComfyUi);

        if (job.Backend == AiStudioBackend.Diffusers && !diffusersFound)
            return Result(AiStudioJobPreflightStatus.MissingDiffusers);

        if (job.Backend == AiStudioBackend.Auto &&
            !((comfyFound && model.Backends.Contains(AiStudioBackend.ComfyUi)) ||
              (diffusersFound && model.Backends.Contains(AiStudioBackend.Diffusers))))
            return Result(AiStudioJobPreflightStatus.MissingBackend);

        // No executable "ready" state: local files are not evidence of a
        // signed, hash-verified runtime or of model-license acceptance.
        return Result(AiStudioJobPreflightStatus.AwaitingVerifiedExecutor);
    }
}
