using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiStudioJobPreflightTests
{
    [Fact]
    public void Does_not_create_workspace_or_launch_any_process()
    {
        using var workspace = new Workspace();
        var result = workspace.Preflight.Inspect(Job());

        Assert.Equal(AiStudioJobPreflightStatus.MissingModel, result.Status);
        Assert.False(Directory.Exists(workspace.Root));
    }

    [Fact]
    public void Unknown_model_is_rejected_before_paths_are_inspected()
    {
        using var workspace = new Workspace();
        var result = workspace.Preflight.Inspect(Job() with { ModelId = "../other" });

        Assert.Equal(AiStudioJobPreflightStatus.UnknownModel, result.Status);
    }

    [Fact]
    public void Unsupported_task_is_reported_without_runtime_execution()
    {
        using var workspace = new Workspace();
        var result = workspace.Preflight.Inspect(Job() with
        {
            Task = AiStudioTaskKind.VideoToVideo
        });

        Assert.Equal(AiStudioJobPreflightStatus.UnsupportedTask, result.Status);
    }

    [Fact]
    public void Unknown_backend_is_rejected()
    {
        using var workspace = new Workspace();
        var result = workspace.Preflight.Inspect(Job() with
        {
            Backend = (AiStudioBackend)999
        });

        Assert.Equal(AiStudioJobPreflightStatus.UnsupportedBackend, result.Status);
    }

    [Fact]
    public void Image_to_image_requires_an_existing_input()
    {
        using var workspace = new Workspace();
        var result = workspace.Preflight.Inspect(Job() with
        {
            Task = AiStudioTaskKind.ImageToImage,
            InputPath = Path.Combine(workspace.Root, "missing.png")
        });

        Assert.Equal(AiStudioJobPreflightStatus.MissingInput, result.Status);
    }

    [Fact]
    public void Supplied_inpaint_mask_must_exist()
    {
        using var workspace = new Workspace();
        var input = Path.Combine(Path.GetTempPath(), "ai-input-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(input, "fixture");
            var result = workspace.Preflight.Inspect(Job() with
            {
                Task = AiStudioTaskKind.InpaintOutpaint,
                InputPath = input,
                MaskPath = input + ".missing"
            });

            Assert.Equal(AiStudioJobPreflightStatus.MissingMask, result.Status);
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public void Output_path_must_be_supplied_but_is_never_created_by_preflight()
    {
        using var workspace = new Workspace();
        var result = workspace.Preflight.Inspect(Job() with
        {
            OutputFolder = ""
        });

        Assert.Equal(AiStudioJobPreflightStatus.MissingOutputFolder, result.Status);
    }

    [Fact]
    public void Reports_missing_python_and_selected_backend_separately()
    {
        using var workspace = new Workspace();
        workspace.AddModel();

        Assert.Equal(
            AiStudioJobPreflightStatus.MissingPython,
            workspace.Preflight.Inspect(Job()).Status);

        workspace.AddPython();

        Assert.Equal(
            AiStudioJobPreflightStatus.MissingComfyUi,
            workspace.Preflight.Inspect(Job() with
            {
                Backend = AiStudioBackend.ComfyUi
            }).Status);

        Assert.Equal(
            AiStudioJobPreflightStatus.MissingDiffusers,
            workspace.Preflight.Inspect(Job() with
            {
                Backend = AiStudioBackend.Diffusers
            }).Status);

        Assert.Equal(
            AiStudioJobPreflightStatus.MissingBackend,
            workspace.Preflight.Inspect(Job()).Status);
    }

    [Fact]
    public void Local_file_presence_never_claims_runtime_execution_is_validated()
    {
        using var workspace = new Workspace();
        workspace.AddModel();
        workspace.AddPython();
        workspace.AddComfyUi();

        var result = workspace.Preflight.Inspect(Job());
        Assert.Equal(AiStudioJobPreflightStatus.AwaitingVerifiedExecutor, result.Status);
        Assert.Contains("execution unavailable", result.Describe("en"));
        Assert.Contains("exécution non disponible", result.Describe("fr"));
    }

    [Fact]
    public void Auto_backend_accepts_detected_diffusers_as_a_dependency_not_as_trust_proof()
    {
        using var workspace = new Workspace();
        workspace.AddModel();
        workspace.AddPython();
        workspace.AddDiffusers();

        Assert.Equal(
            AiStudioJobPreflightStatus.AwaitingVerifiedExecutor,
            workspace.Preflight.Inspect(Job()).Status);
    }

    private static AiStudioJob Job()
        => new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            AiStudioTaskKind.TextToImage,
            "flux2-klein-4b",
            AiStudioBackend.Auto,
            "A test prompt",
            null,
            null,
            Path.GetTempPath(),
            "Queued");

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "ai-job-preflight-" + Guid.NewGuid().ToString("N"));

        public AiStudioJobPreflight Preflight => new(Root);

        public void AddModel()
            => Directory.CreateDirectory(Path.Combine(Root, "models", "flux2-klein-4b"));

        public void AddPython()
        {
            var path = Path.Combine(Root, "runtime", "python", "python.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fixture");
        }

        public void AddComfyUi()
        {
            var path = Path.Combine(Root, "runtime", "comfyui", "main.py");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fixture");
        }

        public void AddDiffusers()
            => Directory.CreateDirectory(
                Path.Combine(Root, "runtime", "diffusers", "src", "diffusers"));

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
