using DlssNrManager.Models;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class GameNvidiaSelectionPolicyTests
{
    private static GenericNvidiaFeatureSelection Select(
        string generation, HardwareOperatingMode mode,
        GenericNvidiaFeatureSelection? requested = null) =>
        GameNvidiaSelectionPolicy.Effective(
            new GpuInfo($"NVIDIA GeForce {generation}", generation, true),
            mode, requested ?? new(true, true, true, true));

    [Theory]
    [InlineData("RTX 50", true, true, true)]
    [InlineData("RTX 40", true, true, false)]
    [InlineData("RTX 30", true, false, false)]
    [InlineData("RTX 20", true, false, false)]
    public void Normal_mode_never_grants_unsupported_features(
        string generation, bool sr, bool fg, bool nr)
    {
        var features = Select(generation, HardwareOperatingMode.Normal);
        Assert.Equal(sr, features.SuperResolution);
        Assert.Equal(fg, features.FrameGeneration);
        Assert.Equal(nr, features.NeuralRendering);
        Assert.True(features.Reflex);
    }

    [Theory]
    [InlineData("RTX 50")]
    [InlineData("RTX 40")]
    [InlineData("RTX 30")]
    [InlineData("RTX 20")]
    public void Compatible_always_disables_FG_NR_but_keeps_supported_SR_and_Reflex(string generation)
    {
        var features = Select(generation, HardwareOperatingMode.Compatible);
        Assert.True(features.SuperResolution);
        Assert.True(features.Reflex);
        Assert.False(features.FrameGeneration);
        Assert.False(features.NeuralRendering);
    }

    [Fact]
    public void Explicitly_disabled_checkboxes_are_never_reenabled()
    {
        var requested = new GenericNvidiaFeatureSelection(false, false, false, false);
        var features = Select("RTX 50", HardwareOperatingMode.Normal, requested);
        Assert.Equal(requested, features);
    }

    [Fact]
    public void All_sixteen_checkbox_combinations_remain_subsets_of_request()
    {
        for (var mask = 0; mask < 16; mask++)
        {
            var requested = new GenericNvidiaFeatureSelection(
                (mask & 1) != 0, (mask & 2) != 0,
                (mask & 4) != 0, (mask & 8) != 0);
            var actual = Select("RTX 50", HardwareOperatingMode.Normal, requested);
            Assert.Equal(requested, actual);
        }
    }

    [Fact]
    public void Unsupported_GPU_cannot_enable_any_feature()
    {
        var result = GameNvidiaSelectionPolicy.Effective(
            new GpuInfo("Non-NVIDIA GPU", "Unknown", false),
            HardwareOperatingMode.Normal, new(true, true, true, true));
        Assert.Equal(new GenericNvidiaFeatureSelection(false, false, false, false), result);
    }

    [Fact]
    public void Main_actions_share_effective_user_selection_instead_of_raw_GPU_capabilities()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DlssNrManager.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "MainWindow.xaml.cs"));
        Assert.Contains("GameNvidiaSelectionPolicy.Effective(", source);
        Assert.Contains("enableNeuralRendering = CaptureGameNvidiaSelection().NeuralRendering;", source);
        Assert.Contains("enableNeuralRendering &= CaptureGameNvidiaSelection().NeuralRendering;", source);
        Assert.Contains("effectiveSelection.NeuralRendering &&", source);
        Assert.Contains("CaptureGameNvidiaSelection() with", source);
        Assert.DoesNotContain("new GenericNvidiaFeatureSelection(\n                            _gpuCapabilities.SuperResolution", source);
    }
}
