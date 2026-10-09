using System.Text.Json;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiStudioJobLifecycleTests
{
    [Fact]
    public void Persisted_job_has_safe_state_transitions_and_verified_result()
    {
        using var f = new Fixture();
        var start = f.CreateQueued();
        var store = new AiStudioJobLifecycleService(f.Jobs);
        var beginning = store.Transition(start.Id, "Starting");
        Assert.Equal("Starting", beginning.Status);
        Assert.NotNull(beginning.UpdatedAt);

        var running = store.Transition(start.Id, "Running");
        Assert.Equal("Running", running.Status);
        Assert.Null(running.ResultFile);

        var file = Path.Combine(f.Outputs, "test-output.png");
        File.WriteAllBytes(file, [137, 80, 78, 71]);
        var completed = store.Transition(start.Id, "Completed", file);
        Assert.Equal(Path.GetFullPath(file), completed.ResultFile);
        Assert.Equal("Completed", completed.Status);

        var persisted = JsonSerializer.Deserialize<AiStudioJob>(
            File.ReadAllText(f.JobPath(start.Id)));
        Assert.NotNull(persisted);
        Assert.Equal("Completed", persisted!.Status);
        Assert.Equal(file, persisted.ResultFile);
        Assert.Throws<InvalidOperationException>(() =>
            store.Transition(start.Id, "Running"));
    }

    [Fact]
    public void Success_requires_existing_nonempty_media_inside_output_directory()
    {
        using var f = new Fixture();
        var queued = f.CreateQueued();
        var store = new AiStudioJobLifecycleService(f.Jobs);
        store.Transition(queued.Id, "Starting");
        store.Transition(queued.Id, "Running");
        Assert.Throws<InvalidDataException>(() =>
            store.Transition(queued.Id, "Completed"));
        Assert.Throws<InvalidDataException>(() =>
            store.Transition(queued.Id, "Completed", Path.Combine(f.Outputs, "nope.png")));

        var empty = Path.Combine(f.Outputs, "zero.png");
        File.WriteAllBytes(empty, []);
        Assert.Throws<InvalidDataException>(() =>
            store.Transition(queued.Id, "Completed", empty));

        var outside = Path.Combine(f.Root, "outside.png");
        File.WriteAllBytes(outside, [1, 2, 3]);
        Assert.Throws<InvalidDataException>(() =>
            store.Transition(queued.Id, "Completed", outside));

        var script = Path.Combine(f.Outputs, "danger.ps1");
        File.WriteAllText(script, "Write-Host unsafe");
        Assert.Throws<InvalidDataException>(() =>
            store.Transition(queued.Id, "Completed", script));

        Assert.Equal("Running", JsonSerializer.Deserialize<AiStudioJob>(
            File.ReadAllText(f.JobPath(queued.Id)))!.Status);
    }

    [Fact]
    public void Explicit_cancellation_and_failure_respect_terminal_states()
    {
        using var f = new Fixture();
        var store = new AiStudioJobLifecycleService(f.Jobs);
        var cancelled = f.CreateQueued();
        Assert.Equal("Cancelled", store.Transition(cancelled.Id, "Cancelled").Status);
        Assert.Throws<InvalidOperationException>(() =>
            store.Transition(cancelled.Id, "Starting"));

        var failed = f.CreateQueued();
        store.Transition(failed.Id, "Starting");
        Assert.Equal("Failed", store.Transition(failed.Id, "Failed").Status);
        Assert.Throws<InvalidOperationException>(() =>
            store.Transition(failed.Id, "Completed", "any.png"));
    }

    [Fact]
    public void Orphaned_jobs_are_interrupted_without_restarting_any_engine()
    {
        using var f = new Fixture();
        var store = new AiStudioJobLifecycleService(f.Jobs);
        var running = f.CreateQueued();
        var waiting = f.CreateQueued();
        store.Transition(running.Id, "Starting");
        store.Transition(running.Id, "Running");

        Assert.Equal(1, store.MarkOrphanedJobsInterrupted());
        Assert.Equal(0, store.MarkOrphanedJobsInterrupted());

        var reread = JsonSerializer.Deserialize<AiStudioJob>(
            File.ReadAllText(f.JobPath(running.Id)));
        Assert.Equal("Interrupted", reread!.Status);
        Assert.Equal("Queued", JsonSerializer.Deserialize<AiStudioJob>(
            File.ReadAllText(f.JobPath(waiting.Id)))!.Status);
        Assert.Equal("Queued", store.Transition(running.Id, "Queued").Status);
    }

    [Fact]
    public void Corrupted_or_mismatched_job_manifests_are_not_overwritten()
    {
        using var f = new Fixture();
        var job = f.CreateQueued();
        var path = f.JobPath(job.Id);
        var original = File.ReadAllText(path);
        var mismatched = job with { Id = Guid.NewGuid() };
        File.WriteAllText(path, JsonSerializer.Serialize(mismatched));
        var store = new AiStudioJobLifecycleService(f.Jobs);
        Assert.Throws<InvalidDataException>(() => store.Transition(job.Id, "Starting"));
        Assert.Equal(JsonSerializer.Serialize(mismatched), File.ReadAllText(path));

        File.WriteAllText(path, original);
        Assert.Equal("Starting", store.Transition(job.Id, "Starting").Status);
    }

    [Fact]
    public void Existing_job_json_without_lifecycle_properties_remains_readable()
    {
        using var f = new Fixture();
        var old = f.CreateQueued();
        Assert.Null(old.UpdatedAt);
        Assert.Null(old.ResultFile);
        Assert.NotNull(new AiStudioJobLifecycleService(f.Jobs)
            .Transition(old.Id, "Starting").UpdatedAt);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "dlssnr-v46-job-" + Guid.NewGuid().ToString("N"));
        public string Jobs => Path.Combine(Root, "jobs");
        public string Outputs => Path.Combine(Root, "outputs");

        public Fixture()
        {
            Directory.CreateDirectory(Jobs);
            Directory.CreateDirectory(Outputs);
        }

        public string JobPath(Guid id) => Path.Combine(Jobs, $"{id:N}.json");

        public AiStudioJob CreateQueued()
        {
            var job = new AiStudioJob(
                Guid.NewGuid(), DateTimeOffset.UtcNow,
                AiStudioTaskKind.TextToImage, "flux2-klein-4b",
                AiStudioBackend.ComfyUi, "A coastal landscape",
                null, null, Outputs, "Queued");
            File.WriteAllText(JobPath(job.Id), JsonSerializer.Serialize(job));
            return job;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
