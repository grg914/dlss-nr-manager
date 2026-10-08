using DlssNrManager.Models;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class HardwareProfileServiceTests
{
    private static HardwareSnapshot Snapshot(
        string name = "NVIDIA GeForce RTX 5060 Ti",
        string generation = "RTX 50",
        string cpu = "AMD Ryzen 7 9700X",
        long? vram = 16303,
        string driver = "617.42")
        => new(new GpuInfo(name, generation, true), cpu, 34359738368,
            vram, driver, true, true, "X64");

    [Fact]
    public void Auto_normal_uses_detected_capabilities()
    {
        var decision = HardwareProfileService.Evaluate(
            Snapshot(), new HardwareProfilePreferences());
        Assert.True(decision.AllowsFrameGeneration);
        Assert.True(decision.AllowsNeuralRendering);
        Assert.False(decision.UsesAutomaticFallback);
    }

    [Fact]
    public void Compatible_mode_disables_optional_generation_features()
    {
        var decision = HardwareProfileService.Evaluate(
            Snapshot(), new HardwareProfilePreferences(
                HardwareProfileChoice.Auto, HardwareOperatingMode.Compatible));
        Assert.True(decision.Capabilities.SuperResolution);
        Assert.False(decision.AllowsFrameGeneration);
        Assert.False(decision.AllowsNeuralRendering);
    }

    [Fact]
    public void Manual_preset_never_unlocks_features_on_older_hardware()
    {
        var decision = HardwareProfileService.Evaluate(
            Snapshot(name: "NVIDIA GeForce RTX 3060", generation: "RTX 30"),
            new HardwareProfilePreferences(HardwareProfileChoice.Rtx5060Ti9700X));
        Assert.True(decision.UsesAutomaticFallback);
        Assert.False(decision.MatchesRequestedPreset);
        Assert.False(decision.AllowsFrameGeneration);
        Assert.False(decision.AllowsNeuralRendering);
    }

    [Fact]
    public void Specific_preset_requires_gpu_cpu_and_sufficient_vram()
    {
        var choice = new HardwareProfilePreferences(HardwareProfileChoice.Rtx5060Ti9700X);
        Assert.True(HardwareProfileService.Evaluate(Snapshot(), choice).MatchesRequestedPreset);
        Assert.True(HardwareProfileService.Evaluate(
            Snapshot(cpu: "Intel Core i7", vram: 16303), choice).UsesAutomaticFallback);
        Assert.True(HardwareProfileService.Evaluate(
            Snapshot(vram: 8192), choice).UsesAutomaticFallback);
        Assert.True(HardwareProfileService.Evaluate(
            Snapshot(vram: null), choice).UsesAutomaticFallback);
    }

    [Theory]
    [InlineData("RTX 50", "Blackwell")]
    [InlineData("RTX 40", "Ada Lovelace")]
    [InlineData("RTX 30", "Ampere")]
    [InlineData("RTX 20", "Turing")]
    public void Rtx_generation_reports_correct_gpu_architecture(
        string generation, string expected)
    {
        Assert.Equal(expected, Snapshot(generation: generation).GpuArchitecture);
    }

    [Fact]
    public void Driver_hardware_or_api_change_invalidates_fingerprint()
    {
        var first = Snapshot();
        Assert.NotEqual(first.Fingerprint, (first with { NvidiaDriverVersion = "617.43" }).Fingerprint);
        Assert.NotEqual(first.Fingerprint, (first with { GpuVramMiB = 8192 }).Fingerprint);
        Assert.NotEqual(first.Fingerprint, (first with { VulkanLoaderPresent = false }).Fingerprint);
    }
}
