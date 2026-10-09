using System.Text.Json;

namespace DlssNrManager.Services;

/// <summary>
/// Offline, process-independent bookkeeping for AI Studio Jobs. No model or
/// Python execution is authorized by changing a status in this store.
/// </summary>
public sealed class AiStudioJobLifecycleService
{
    private readonly string _jobsRoot;
    private readonly object _gate = new();

    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Queued"] = ["Starting", "Cancelled"],
            ["Starting"] = ["Running", "Failed", "Cancelling", "Interrupted"],
            ["Running"] = ["Completed", "Failed", "Cancelling", "Interrupted"],
            ["Cancelling"] = ["Cancelled", "Failed", "Interrupted"],
            ["Interrupted"] = ["Queued", "Cancelled"],
            ["Completed"] = [],
            ["Cancelled"] = [],
            ["Failed"] = []
        };

    private static readonly HashSet<string> SupportedResultExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".mp4", ".webm"
        };

    public AiStudioJobLifecycleService(string jobsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobsRoot);
        _jobsRoot = Path.GetFullPath(jobsRoot);
    }

    /// <summary>
    /// A completed job MUST point at a real non-empty image/video file inside
    /// the original output directory; a successful HTTP response is not proof.
    /// </summary>
    public AiStudioJob Transition(
        Guid jobId, string nextStatus, string? resultPath = null)
    {
        if (jobId == Guid.Empty || !AllowedTransitions.ContainsKey(nextStatus))
            throw new ArgumentException("Unknown job identity or status.");

        lock (_gate)
        {
            var path = GetJobPath(jobId);
            var job = ReadJob(path, jobId);

            if (!AllowedTransitions.TryGetValue(job.Status, out var permitted) ||
                !permitted.Contains(nextStatus, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Invalid AI Studio job transition {job.Status} -> {nextStatus}.");

            string? verifiedResult = null;
            if (nextStatus == "Completed")
                verifiedResult = VerifyResult(job.OutputFolder, resultPath);
            else if (resultPath is not null)
                throw new ArgumentException(
                    "Only a completed job may record an output file.", nameof(resultPath));

            var updated = job with
            {
                Status = nextStatus,
                UpdatedAt = DateTimeOffset.UtcNow,
                ResultFile = verifiedResult
            };
            EnsureSafeDirectory();
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(updated));
            return updated;
        }
    }

    /// <summary>
    /// Explicit recovery after an app/process crash. Never silently resume GPU
    /// execution or mark a job completed. Queued jobs remain queued.
    /// </summary>
    public int MarkOrphanedJobsInterrupted()
    {
        lock (_gate)
        {
            EnsureSafeDirectory();
            if (!Directory.Exists(_jobsRoot))
                return 0;

            var changed = 0;
            foreach (var file in Directory.EnumerateFiles(
                         _jobsRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (ManagedPathSafety.HasReparsePointOnPath(file))
                    continue;

                if (!Guid.TryParseExact(
                        Path.GetFileNameWithoutExtension(file), "N", out var id))
                    continue;

                AiStudioJob job;
                try
                {
                    job = ReadJob(file, id);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException
                    or UnauthorizedAccessException or JsonException)
                {
                    // Preserve malformed files for a user-requested diagnosis.
                    continue;
                }

                if (job.Status is not ("Starting" or "Running" or "Cancelling"))
                    continue;

                var updated = job with
                {
                    Status = "Interrupted",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ResultFile = null
                };
                AtomicFile.WriteAllText(file, JsonSerializer.Serialize(updated));
                changed++;
            }
            return changed;
        }
    }

    private string GetJobPath(Guid id)
    {
        EnsureSafeDirectory();
        return Path.Combine(_jobsRoot, $"{id:N}.json");
    }

    private void EnsureSafeDirectory()
    {
        if (ManagedPathSafety.HasReparsePointOnPath(_jobsRoot))
            throw new IOException("AI Studio Jobs directory is redirected.");
    }

    private static AiStudioJob ReadJob(string path, Guid expectedId)
    {
        if (ManagedPathSafety.HasReparsePointOnPath(path))
            throw new IOException("AI Studio job file is redirected.");

        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 20 or > 128 * 1024)
            throw new InvalidDataException("Job file is missing or oversized.");

        var job = JsonSerializer.Deserialize<AiStudioJob>(File.ReadAllText(path));
        if (job is null || job.Id != expectedId ||
            !AllowedTransitions.ContainsKey(job.Status))
            throw new InvalidDataException("Invalid AI Studio job manifest.");
        return job;
    }

    private static string VerifyResult(string outputFolder, string? resultPath)
    {
        if (string.IsNullOrWhiteSpace(outputFolder) ||
            string.IsNullOrWhiteSpace(resultPath))
            throw new InvalidDataException("Completed job has no output file.");

        var root = Path.GetFullPath(outputFolder);
        var absolute = Path.GetFullPath(resultPath);

        var relative = Path.GetRelativePath(root, absolute);
        if (Path.IsPathRooted(relative) || relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            ManagedPathSafety.HasReparsePointOnPath(root) ||
            ManagedPathSafety.HasReparsePointOnPath(absolute) ||
            !SupportedResultExtensions.Contains(Path.GetExtension(absolute)))
            throw new InvalidDataException("Job result escapes approved output directory.");

        var result = new FileInfo(absolute);
        if (!result.Exists || result.Length <= 0)
            throw new InvalidDataException("Job result is missing or empty.");

        return absolute;
    }
}
