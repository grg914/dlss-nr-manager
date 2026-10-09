using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class LocalAiStudioServiceTests
{
    [Fact]
    public void Runtime_summary_never_equates_file_presence_with_verified_execution()
    {
        var service = new LocalAiStudioService();

        var english = service.GetRuntimeSummary("en");
        var french = service.GetRuntimeSummary("fr");

        Assert.Contains("Execution unavailable until", english);
        Assert.Contains("Exécution indisponible tant que", french);
        Assert.DoesNotContain("installed", english);
        Assert.DoesNotContain("ready", english);
        Assert.DoesNotContain("installé", french);
        Assert.Contains("Python:", english);
        Assert.Contains("ComfyUI:", english);
        Assert.Contains("Diffusers:", english);
    }

    [Fact]
    public void Model_status_reports_local_files_without_claiming_verified_installation()
    {
        var service = new LocalAiStudioService();
        var model = Assert.Single(LocalAiStudioService.Models, x => x.Id == "flux2-klein-4b");

        var english = service.GetModelStatus(model, "en");
        var french = service.GetModelStatus(model, "fr");

        Assert.Contains("Files", english);
        Assert.Contains("Fichiers", french);
        Assert.DoesNotContain("installed", english.ToLowerInvariant());
        Assert.DoesNotContain("installé", french.ToLowerInvariant());
        Assert.Contains(model.License, english);
        Assert.Contains(model.License, french);
    }

    [Fact]
    public void Text_to_image_defaults_include_flux2_klein()
    {
        var service = new LocalAiStudioService();
        var models = service.GetModels(
            AiStudioTaskKind.TextToImage);

        Assert.Contains(
            models,
            x =>
                x.Id == "flux2-klein-4b" &&
                x.Tier == AiStudioModelTier.Recommended);
    }

    [Fact]
    public void Text_to_video_defaults_include_wan22_5b()
    {
        var service = new LocalAiStudioService();
        var models = service.GetModels(
            AiStudioTaskKind.TextToVideo);

        Assert.Contains(
            models,
            x =>
                x.Id == "wan2.2-ti2v-5b" &&
                x.Tier == AiStudioModelTier.Recommended);
    }

    [Fact]
    public void Maximum_quality_models_remain_selectable()
    {
        Assert.Contains(
            LocalAiStudioService.Models,
            x =>
                x.Id == "flux2-dev" &&
                x.Tier == AiStudioModelTier.MaximumQuality);

        Assert.Contains(
            LocalAiStudioService.Models,
            x =>
                x.Id == "qwen-image-2.1" &&
                x.Tier == AiStudioModelTier.MaximumQuality);

        Assert.Contains(
            LocalAiStudioService.Models,
            x =>
                x.Id == "wan2.2-t2v-a14b" &&
                x.Tier == AiStudioModelTier.MaximumQuality);
    }

    [Fact]
    public void Restricted_weight_licenses_are_not_auto_redistributable()
    {
        var fluxDev = Assert.Single(
            LocalAiStudioService.Models,
            x => x.Id == "flux2-dev");
        var qwen = Assert.Single(
            LocalAiStudioService.Models,
            x => x.Id == "qwen-image-2.1");
        var ltx = Assert.Single(
            LocalAiStudioService.Models,
            x => x.Id == "ltx-2.5");

        Assert.False(fluxDev.ManagerOwnedRedistributionAllowed);
        Assert.False(qwen.ManagerOwnedRedistributionAllowed);
        Assert.False(ltx.ManagerOwnedRedistributionAllowed);
    }

    [Fact]
    public void Inpainting_has_dedicated_sdxl_fallback()
    {
        var service = new LocalAiStudioService();
        var models = service.GetModels(
            AiStudioTaskKind.InpaintOutpaint);

        Assert.Contains(
            models,
            x => x.Id == "sdxl-inpaint-1.0");
    }

    [Fact]
    public void Video_to_video_excludes_image_only_models()
    {
        var service = new LocalAiStudioService();
        var models = service.GetModels(
            AiStudioTaskKind.VideoToVideo);

        Assert.DoesNotContain(
            models,
            x => x.Id == "flux2-klein-4b");
        Assert.Contains(
            models,
            x => x.Id == "wan2.2-animate-14b");
    }
}
