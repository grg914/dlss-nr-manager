using DlssNrManager.Models;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class GpuAdaptiveProfileServiceTests
{
    private static HardwareSnapshot Gpu(string name, string generation, long? vram = 8192)
        => new(new GpuInfo(name, generation, name.StartsWith("NVIDIA", StringComparison.Ordinal)),
            "Any CPU", 16UL * 1024 * 1024 * 1024, vram, "Unknown", true, true, "X64");

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5060 Ti", "RTX 50", true, true)]
    [InlineData("NVIDIA GeForce RTX 4070", "RTX 40", true, false)]
    [InlineData("NVIDIA GeForce RTX 3070", "RTX 30", false, false)]
    [InlineData("NVIDIA GeForce RTX 2070", "RTX 20", false, false)]
    public void Auto_profiles_follow_gpu_capabilities_not_one_fixed_cpu(
        string name, string generation, bool fg, bool mfg)
    {
        var result = GpuAdaptiveProfileService.Evaluate(
            Gpu(name, generation),
            new HardwareProfilePreferences(Goal: GpuOptimizationGoal.Balanced));

        Assert.Equal(GpuAdaptiveMode.Auto, result.Mode);
        Assert.True(result.SuperResolution);
        Assert.Equal(fg, result.FrameGeneration);
        Assert.Equal(mfg, result.MultiFrameGenerationEligible);
        Assert.False(result.NeuralRendering);
    }

    [Fact]
    public void Manual_choices_are_clamped_to_actual_hardware()
    {
        var prefs = new HardwareProfilePreferences(
            AdaptiveMode: GpuAdaptiveMode.Manual,
            ManualOptions: new GpuManualOptions(false, true, false, true));
        var result = GpuAdaptiveProfileService.Evaluate(
            Gpu("NVIDIA GeForce RTX 3070", "RTX 30"), prefs);
        Assert.False(result.SuperResolution);
        Assert.False(result.FrameGeneration);
        Assert.False(result.Reflex);
        Assert.False(result.NeuralRendering);
    }

    [Fact]
    public void Manual_neural_rendering_is_explicit_opt_in_on_supported_gpu()
    {
        var snapshot = Gpu("NVIDIA GeForce RTX 5060 Ti", "RTX 50");
        var off = GpuAdaptiveProfileService.Evaluate(
            snapshot, new HardwareProfilePreferences(AdaptiveMode: GpuAdaptiveMode.Manual));
        var on = GpuAdaptiveProfileService.Evaluate(
            snapshot, new HardwareProfilePreferences(
                AdaptiveMode: GpuAdaptiveMode.Manual,
                ManualOptions: new GpuManualOptions(NeuralRendering: true)));
        Assert.False(off.NeuralRendering);
        Assert.True(on.NeuralRendering);
    }

    [Fact]
    public void Compatible_never_requests_optional_generation_even_if_manually_checked()
    {
        var result = GpuAdaptiveProfileService.Evaluate(
            Gpu("NVIDIA GeForce RTX 5090", "RTX 50"),
            new HardwareProfilePreferences(
                AdaptiveMode: GpuAdaptiveMode.Compatible,
                ManualOptions: new GpuManualOptions(true, true, true, true)));
        Assert.True(result.SuperResolution);
        Assert.False(result.FrameGeneration);
        Assert.False(result.NeuralRendering);
    }

    [Fact]
    public void Legacy_compatible_flag_remains_fail_closed()
    {
        var result = GpuAdaptiveProfileService.Evaluate(
            Gpu("NVIDIA GeForce RTX 5090", "RTX 50"),
            new HardwareProfilePreferences(Mode: HardwareOperatingMode.Compatible));
        Assert.Equal(GpuAdaptiveMode.Compatible, result.Mode);
        Assert.False(result.FrameGeneration);
    }

    [Fact]
    public void Priority_changes_only_recommendations_not_driver_configuration()
    {
        var snapshot = Gpu("NVIDIA GeForce RTX 4080", "RTX 40");
        Assert.Equal("Quality", GpuAdaptiveProfileService.Evaluate(
            snapshot, new HardwareProfilePreferences(Goal: GpuOptimizationGoal.Quality)).SuggestedDlssMode);
        Assert.Equal("Performance", GpuAdaptiveProfileService.Evaluate(
            snapshot, new HardwareProfilePreferences(Goal: GpuOptimizationGoal.Performance)).SuggestedDlssMode);
    }

    [Fact]
    public void Unrecognized_gpu_never_unlocks_rtx()
    {
        var result = GpuAdaptiveProfileService.Evaluate(
            Gpu("AMD Radeon RX 7900 XT", "Unknown"),
            new HardwareProfilePreferences(AdaptiveMode: GpuAdaptiveMode.Manual,
                ManualOptions: new GpuManualOptions(true, true, true, true)));
        Assert.False(result.SuperResolution);
        Assert.False(result.FrameGeneration);
        Assert.False(result.Reflex);
        Assert.False(result.NeuralRendering);
    }
}
