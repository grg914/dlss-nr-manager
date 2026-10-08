using Xunit;
using DlssNrManager.Services;

namespace DlssNrManager.Tests;

public sealed class VlcVideoEnhancementServiceTests
{
    [Fact]
    public void Windowed_x4_builds_enabled_vsr_hdr_and_zoom_arguments()
    {
        var args = VlcVideoEnhancementService.BuildArguments(
            @"C:\media\sample.mp4",
            new VlcLaunchOptions(
                SuperResolution: true,
                ArtifactReduction: false,
                HdrEnabled: true,
                HdrMode: VlcHdrMode.Generate,
                CustomScaleEnabled: true,
                Scale: VlcScaleMode.X4,
                Fullscreen: false,
                ShowStatusOverlay: true));

        Assert.Contains("--vout=direct3d11", args);
        Assert.Contains("--d3d11-upscale-mode=super", args);
        Assert.Contains("--d3d11-hdr-mode=generate", args);
        Assert.Contains("--no-autoscale", args);
        Assert.Contains("--zoom=4", args);
        Assert.DoesNotContain("--fullscreen", args);
        Assert.Equal(@"C:\media\sample.mp4", args[^1]);
    }

    [Fact]
    public void Fullscreen_targets_display_and_ignores_custom_window_scale()
    {
        var args = VlcVideoEnhancementService.BuildArguments(
            @"C:\media\sample.mp4",
            new VlcLaunchOptions(
                SuperResolution: true,
                ArtifactReduction: false,
                HdrEnabled: true,
                HdrMode: VlcHdrMode.Auto,
                CustomScaleEnabled: true,
                Scale: VlcScaleMode.X4,
                Fullscreen: true,
                ShowStatusOverlay: true));

        Assert.Contains("--fullscreen", args);
        Assert.Contains("--autoscale", args);
        Assert.DoesNotContain("--zoom=4", args);
        Assert.DoesNotContain("--no-autoscale", args);
    }

    [Fact]
    public void Independent_toggles_can_disable_vsr_hdr_and_custom_scale()
    {
        var args = VlcVideoEnhancementService.BuildArguments(
            @"C:\media\sample.mp4",
            new VlcLaunchOptions(
                SuperResolution: false,
                ArtifactReduction: false,
                HdrEnabled: false,
                HdrMode: VlcHdrMode.Generate,
                CustomScaleEnabled: false,
                Scale: VlcScaleMode.X4,
                Fullscreen: false,
                ShowStatusOverlay: true));

        Assert.Contains("--vout=direct3d11", args);
        Assert.DoesNotContain("--d3d11-upscale-mode=super", args);
        Assert.Contains("--d3d11-upscale-mode=linear", args);
        Assert.Contains("--d3d11-hdr-mode=auto", args);
        Assert.Contains("--autoscale", args);
        Assert.DoesNotContain("--no-autoscale", args);
        Assert.DoesNotContain("--zoom=4", args);
        Assert.Contains("--no-fullscreen", args);
        Assert.DoesNotContain("--fullscreen", args);
    }

    [Fact]
    public void Hdr_can_be_enabled_without_vsr_or_custom_scale()
    {
        var args = VlcVideoEnhancementService.BuildArguments(
            @"C:\media\sample.mp4",
            new VlcLaunchOptions(
                SuperResolution: false,
                ArtifactReduction: false,
                HdrEnabled: true,
                HdrMode: VlcHdrMode.Generate,
                CustomScaleEnabled: false,
                Scale: VlcScaleMode.X1,
                Fullscreen: false,
                ShowStatusOverlay: true));

        Assert.Contains("--d3d11-upscale-mode=linear", args);
        Assert.Contains("--d3d11-hdr-mode=generate", args);
        Assert.Contains("--autoscale", args);
        Assert.Contains("--no-fullscreen", args);
        Assert.DoesNotContain("--zoom=1", args);
    }
    [Fact]
    public void Artifact_reduction_can_request_vsr_path_independently()
    {
        var args = VlcVideoEnhancementService.BuildArguments(
            @"C:\media\sample.mp4",
            new VlcLaunchOptions(
                SuperResolution: false,
                ArtifactReduction: true,
                HdrEnabled: false,
                HdrMode: VlcHdrMode.Generate,
                CustomScaleEnabled: true,
                Scale: VlcScaleMode.X2,
                Fullscreen: false,
                ShowStatusOverlay: true));

        Assert.Contains("--d3d11-upscale-mode=super", args);
        Assert.Contains("--d3d11-hdr-mode=auto", args);
        Assert.Contains("--zoom=2", args);
    }
    [Fact]
    public void Status_overlay_is_rendered_top_right_for_vsr_and_hdr()
    {
        var args = VlcVideoEnhancementService.BuildArguments(
            @"C:\media\sample.mp4",
            new VlcLaunchOptions(
                SuperResolution: true,
                ArtifactReduction: false,
                HdrEnabled: true,
                HdrMode: VlcHdrMode.Generate,
                CustomScaleEnabled: true,
                Scale: VlcScaleMode.X2,
                Fullscreen: false,
                ShowStatusOverlay: true));

        Assert.Contains("--sub-source=marq", args);
        Assert.Contains("--marq-marquee=● VSR • HDR", args);
        Assert.Contains("--marq-position=6", args);
        Assert.Contains("--marq-opacity=220", args);
        Assert.Contains("--marq-size=18", args);
    }

    [Fact]
    public void Status_overlay_is_absent_when_disabled()
    {
        var args = VlcVideoEnhancementService.BuildArguments(
            @"C:\media\sample.mp4",
            new VlcLaunchOptions(
                SuperResolution: true,
                ArtifactReduction: false,
                HdrEnabled: true,
                HdrMode: VlcHdrMode.Generate,
                CustomScaleEnabled: false,
                Scale: VlcScaleMode.X1,
                Fullscreen: false,
                ShowStatusOverlay: false));

        Assert.DoesNotContain("--sub-source=marq", args);
        Assert.DoesNotContain(args, x => x.StartsWith("--marq-", StringComparison.Ordinal));
    }

}
