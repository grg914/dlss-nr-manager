using System.Text.Json;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiStudioFlux2Fp8OfflineTests
{
    [Fact]
    public void Generated_text_to_image_graph_has_only_expected_pinned_core_nodes()
    {
        var workflow = AiStudioFlux2Fp8WorkflowService.BuildTextToImage(
            "Illustration: \"Nordic\"\n{never_a_node}", 768, 1024, 1234);
        using var json = JsonDocument.Parse(workflow);
        var nodes = json.RootElement;
        Assert.Equal(13, nodes.EnumerateObject().Count());
        Assert.Equal("UNETLoader", Type(nodes, "1"));
        Assert.Equal(AiStudioFlux2Fp8WorkflowService.ModelName,
            nodes.GetProperty("1").GetProperty("inputs").GetProperty("unet_name").GetString());
        Assert.Equal(AiStudioFlux2Fp8WorkflowService.EncoderName,
            nodes.GetProperty("2").GetProperty("inputs").GetProperty("clip_name").GetString());
        Assert.Equal("flux2",
            nodes.GetProperty("2").GetProperty("inputs").GetProperty("type").GetString());
        Assert.Equal(AiStudioFlux2Fp8WorkflowService.VaeName,
            nodes.GetProperty("3").GetProperty("inputs").GetProperty("vae_name").GetString());
        Assert.Equal("Illustration: \"Nordic\"\n{never_a_node}",
            nodes.GetProperty("4").GetProperty("inputs").GetProperty("text").GetString());
        Assert.Equal("SaveImage", Type(nodes, "13"));
        Assert.Equal(768,
            nodes.GetProperty("6").GetProperty("inputs").GetProperty("width").GetInt32());
        Assert.Equal("CFGGuider", Type(nodes, "10"));
        Assert.Equal("SamplerCustomAdvanced", Type(nodes, "11"));
        Assert.DoesNotContain("CheckpointLoaderSimple", workflow);
        Assert.DoesNotContain("CustomPython", workflow);
    }

    [Fact]
    public void Image_edit_uses_reference_latent_without_remote_or_custom_nodes()
    {
        using var json = JsonDocument.Parse(
            AiStudioFlux2Fp8WorkflowService.BuildImageEdit(
                "Change color", "input_photo.webp", 1024, 1024));
        var nodes = json.RootElement;
        Assert.Equal(17, nodes.EnumerateObject().Count());
        Assert.Equal("LoadImage", Type(nodes, "14"));
        Assert.Equal("ReferenceLatent", Type(nodes, "16"));
        Assert.Equal("ReferenceLatent", Type(nodes, "17"));
        Assert.Equal("input_photo.webp",
            nodes.GetProperty("14").GetProperty("inputs").GetProperty("image").GetString());
        Assert.Equal("16",
            nodes.GetProperty("10").GetProperty("inputs").GetProperty("positive")[0].GetString());
        Assert.Equal("17",
            nodes.GetProperty("10").GetProperty("inputs").GetProperty("negative")[0].GetString());
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("C:\\temp\\secret.png")]
    [InlineData("secret.png?url=https://example.com/")]
    [InlineData("malicious.svg")]
    [InlineData("")]
    public void Image_edits_reject_unreviewed_input_names(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            AiStudioFlux2Fp8WorkflowService.BuildImageEdit("Prompt", path));
    }

    [Fact]
    public void Prompt_and_geometry_are_bounded()
    {
        Assert.Throws<ArgumentException>(() =>
            AiStudioFlux2Fp8WorkflowService.BuildTextToImage(""));
        Assert.Throws<ArgumentException>(() =>
            AiStudioFlux2Fp8WorkflowService.BuildTextToImage(new string('x', 2049)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AiStudioFlux2Fp8WorkflowService.BuildTextToImage("ok", 257, 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AiStudioFlux2Fp8WorkflowService.BuildTextToImage("ok", 4096, 1024));
    }

    [Fact]
    public void Offline_import_rejects_raw_7z_volumes_and_install_path_escape()
    {
        using var t = new TempManifest();
        var manifest = ValidManifest() with
        {
            Archive = new AiStudioPackageArchive(
                "flux2-klein-4b-0.1.0.7z.001", 32, new string('a', 64), "7z"),
            Chunks = new[]
            {
                new AiStudioPackageChunk(1,
                    "flux2-klein-4b-fp8.7z.001", 32, new string('b', 64))
            }
        };
        Assert.Throws<InvalidDataException>(() =>
            AiStudioFlux2OfflineImportService.ReadAndValidateManifest(t.Write(manifest)));

        manifest = ValidManifest() with { InstallRelativePath = "../outputs" };
        Assert.Throws<InvalidDataException>(() =>
            AiStudioFlux2OfflineImportService.ReadAndValidateManifest(t.Write(manifest)));
    }

    [Fact]
    public void Offline_manifest_requires_complete_ordered_parts_and_matching_total()
    {
        using var t = new TempManifest();
        var manifest = ValidManifest();
        Assert.Equal(32,
            AiStudioFlux2OfflineImportService.ReadAndValidateManifest(t.Write(manifest))
                .Archive.Size);
        manifest = manifest with
        {
            Chunks = new[]
            {
                new AiStudioPackageChunk(
                    2, "flux2-klein-4b-0.1.0.part002", 32, new string('c', 64))
            }
        };
        Assert.Throws<InvalidDataException>(() =>
            AiStudioFlux2OfflineImportService.ReadAndValidateManifest(t.Write(manifest)));
    }

    [Fact]
    public void Selected_generation_settings_are_embedded_in_api_graph()
    {
        var settings = new AiStudioImageSettings(576, 1024, 12, 98765);
        settings.Validate();
        using var json = JsonDocument.Parse(
            AiStudioFlux2Fp8WorkflowService.BuildTextToImage("Nordic fjord", settings));
        var root = json.RootElement;
        Assert.Equal(576, root.GetProperty("6").GetProperty("inputs").GetProperty("width").GetInt32());
        Assert.Equal(1024, root.GetProperty("6").GetProperty("inputs").GetProperty("height").GetInt32());
        Assert.Equal(12, root.GetProperty("7").GetProperty("inputs").GetProperty("steps").GetInt32());
        Assert.Equal(98765L, root.GetProperty("8").GetProperty("inputs").GetProperty("noise_seed").GetInt64());
    }

    [Fact]
    public void Rejects_excessive_steps_pixels_and_seeds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AiStudioImageSettings(1024, 1024, 33, 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AiStudioImageSettings(2048, 2048, 4, -1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AiStudioImageSettings(2050, 1024, 4, 1).Validate());
    }

    [Fact]
    public void Old_queued_job_json_remains_readable_without_image_options()
    {
        var old = new AiStudioJob(
            Guid.NewGuid(), DateTimeOffset.UtcNow,
            AiStudioTaskKind.TextToImage, "flux2-klein-4b", AiStudioBackend.ComfyUi,
            "A fjord", null, null, "outputs", "Queued");
        var json = JsonSerializer.Serialize(old, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        Assert.DoesNotContain("ImageSettings", json);
        var reread = JsonSerializer.Deserialize<AiStudioJob>(json);
        Assert.NotNull(reread);
        Assert.Null(reread!.ImageSettings);

        var newJob = old with { ImageSettings = new AiStudioImageSettings(768, 768, 4, 777) };
        var newRead = JsonSerializer.Deserialize<AiStudioJob>(JsonSerializer.Serialize(newJob));
        Assert.Equal(newJob.ImageSettings, newRead!.ImageSettings);
    }

    [Fact]
    public async Task Missing_installed_model_is_never_reported_as_sha_verified()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-flux-" + Guid.NewGuid().ToString("N"));
        Assert.False(await AiStudioFlux2OfflineImportService.VerifyInstalledAsync(path));
    }

    private static AiStudioPackageManifest ValidManifest() =>
        new(1, "flux2-klein-4b", "FLUX.2", "0.1.0",
            "ai-studio-flux2-klein-4b-0.1.0",
            "models/flux2-klein-4b",
            "Apache-2.0", "https://huggingface.co/black-forest-labs/FLUX.2-klein-4b-fp8",
            new AiStudioPackageArchive(
                "flux2-klein-4b-0.1.0.zip", 32, new string('a', 64), "zip"),
            new[] { new AiStudioPackageChunk(
                1, "flux2-klein-4b-0.1.0.part001", 32, new string('b', 64)) });

    private static string Type(JsonElement root, string id) =>
        root.GetProperty(id).GetProperty("class_type").GetString()!;

    private sealed class TempManifest : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "dlssnr-flux-fixture-" + Guid.NewGuid().ToString("N"));
        public string Write(AiStudioPackageManifest manifest)
        {
            Directory.CreateDirectory(_dir);
            var path = Path.Combine(_dir, "flux2-klein-4b.manifest.json");
            File.WriteAllText(path, JsonSerializer.Serialize(manifest));
            return path;
        }
        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }
    }
}
