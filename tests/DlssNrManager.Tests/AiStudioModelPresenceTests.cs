using System.Text.Json;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiStudioModelPresenceTests
{
    private static readonly AiStudioModelDescriptor Flux =
        LocalAiStudioService.Models.Single(x => x.Id == "flux2-klein-4b");

    private static readonly AiStudioModelDescriptor Restricted =
        LocalAiStudioService.Models.Single(x => x.Id == "flux2-dev");

    [Fact]
    public void Missing_and_empty_directories_are_not_installed()
    {
        using var f = new Fixture();
        Assert.Equal(AiStudioLocalModelState.Missing,
            LocalAiStudioService.InspectModelFiles(Flux, f.Path));
        Directory.CreateDirectory(f.Path);
        Assert.Equal(AiStudioLocalModelState.Incomplete,
            LocalAiStudioService.InspectModelFiles(Flux, f.Path));
        File.WriteAllText(System.IO.Path.Combine(f.Path, "README.md"), "partial");
        Assert.Equal(AiStudioLocalModelState.Incomplete,
            LocalAiStudioService.InspectModelFiles(Flux, f.Path));
    }

    [Fact]
    public void Flux_requires_all_three_nonempty_weights_and_valid_receipt()
    {
        using var f = new Fixture();
        WriteWeight(f.Path, "diffusion_models/flux-2-klein-4b-fp8.safetensors", new byte[] { 7 });
        WriteWeight(f.Path, "text_encoders/qwen_3_4b.safetensors", new byte[] { 4 });
        Assert.Equal(AiStudioLocalModelState.Incomplete,
            LocalAiStudioService.InspectModelFiles(Flux, f.Path));

        WriteWeight(f.Path, "vae/flux2-vae.safetensors", Array.Empty<byte>());
        WriteReceipt(f.Path, Flux.Id);
        Assert.Equal(AiStudioLocalModelState.Incomplete,
            LocalAiStudioService.InspectModelFiles(Flux, f.Path));

        WriteWeight(f.Path, "vae/flux2-vae.safetensors", new byte[] { 1 });
        Assert.Equal(AiStudioLocalModelState.FilesPresentUnverified,
            LocalAiStudioService.InspectModelFiles(Flux, f.Path));

        // Cheap metadata checks cannot assert that these dummy bytes match a
        // trusted digest. Full hash verification remains an explicit action.
    }

    [Fact]
    public void Managed_models_without_receipt_or_with_wrong_receipt_are_incomplete()
    {
        using var f = new Fixture();
        WriteWeight(f.Path, "diffusion_models/flux-2-klein-4b-fp8.safetensors", new byte[] { 1 });
        WriteWeight(f.Path, "text_encoders/qwen_3_4b.safetensors", new byte[] { 1 });
        WriteWeight(f.Path, "vae/flux2-vae.safetensors", new byte[] { 1 });
        Assert.Equal(AiStudioLocalModelState.Incomplete,
            LocalAiStudioService.InspectModelFiles(Flux, f.Path));

        WriteReceipt(f.Path, "wrong-model");
        Assert.Equal(AiStudioLocalModelState.Incomplete,
            LocalAiStudioService.InspectModelFiles(Flux, f.Path));

        WriteReceipt(f.Path, Flux.Id);
        Assert.Equal(AiStudioLocalModelState.FilesPresentUnverified,
            LocalAiStudioService.InspectModelFiles(Flux, f.Path));
    }

    [Fact]
    public void Restricted_manual_models_never_require_manager_receipt()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(f.Path);
        Assert.Equal(AiStudioLocalModelState.Incomplete,
            LocalAiStudioService.InspectModelFiles(Restricted, f.Path));
        WriteWeight(f.Path, "transformer/model.safetensors", Array.Empty<byte>());
        Assert.Equal(AiStudioLocalModelState.Incomplete,
            LocalAiStudioService.InspectModelFiles(Restricted, f.Path));
        WriteWeight(f.Path, "transformer/model.safetensors", new byte[] { 1, 2 });
        Assert.Equal(AiStudioLocalModelState.FilesPresentUnverified,
            LocalAiStudioService.InspectModelFiles(Restricted, f.Path));
    }

    [Fact]
    public void Configuration_and_license_files_are_not_model_weights()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(f.Path);
        File.WriteAllText(System.IO.Path.Combine(f.Path, "config.json"), "{}");
        File.WriteAllText(System.IO.Path.Combine(f.Path, "LICENSE"), "test");
        Assert.Equal(AiStudioLocalModelState.Incomplete,
            LocalAiStudioService.InspectModelFiles(Restricted, f.Path));
    }

    private static void WriteReceipt(string root, string modelId)
    {
        File.WriteAllText(
            System.IO.Path.Combine(root, AiStudioPackageService.LocalReceiptFile),
            JsonSerializer.Serialize(new AiStudioPackageReceipt(
                modelId, "0.1.0", $"ai-studio-{modelId}-0.1.0", new string('a', 64))));
    }

    private static void WriteWeight(string root, string relative, byte[] bytes)
    {
        var path = System.IO.Path.Combine(root, relative.Replace('/',
            System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ai-presence-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
