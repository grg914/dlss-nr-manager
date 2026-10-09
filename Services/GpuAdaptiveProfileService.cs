using DlssNrManager.Models;

namespace DlssNrManager.Services;

// These settings are recommendations for the manager's game controls, NOT
// NVIDIA Control Panel, NVIDIA App, GPU clocks or global driver profiles.
public enum GpuAdaptiveMode
{
    Auto = 0,
    Manual = 1,
    Compatible = 2
}

public enum GpuOptimizationGoal
{
    Quality = 0,
    Balanced = 1,
    Performance = 2
}

public sealed record GpuManualOptions(
    bool SuperResolution = true,
    bool FrameGeneration = false,
    bool Reflex = true,
    bool NeuralRendering = false);

public sealed record GpuAdaptiveRecommendation(
    GpuAdaptiveMode Mode,
    GpuOptimizationGoal Goal,
    string SuggestedDlssMode,
    bool SuperResolution,
    bool FrameGeneration,
    bool Reflex,
    bool NeuralRendering,
    bool MultiFrameGenerationEligible,
    string Explanation);

public static class GpuAdaptiveProfileService
{
    public static GpuAdaptiveRecommendation Evaluate(
        HardwareSnapshot snapshot,
        HardwareProfilePreferences preferences)
    {
        var capabilities = GpuCapabilityService.Evaluate(snapshot.Gpu);
        var mode = preferences.AdaptiveMode;
        // Preserve the restrictive meaning of existing v4 compatible preferences.
        if (preferences.Mode == HardwareOperatingMode.Compatible)
            mode = GpuAdaptiveMode.Compatible;

        var goal = preferences.Goal;
        var manual = preferences.ManualOptions ?? new GpuManualOptions();
        var sr = capabilities.SuperResolution &&
                 (mode != GpuAdaptiveMode.Manual || manual.SuperResolution);
        var fg = capabilities.FrameGeneration && (mode switch
        {
            GpuAdaptiveMode.Manual => manual.FrameGeneration,
            GpuAdaptiveMode.Compatible => false,
            _ => goal != GpuOptimizationGoal.Quality
        });
        var reflex = capabilities.IsSupportedRtx &&
                     (mode != GpuAdaptiveMode.Manual || manual.Reflex);
        // NR requires an explicit opt-in; an RTX 50 GPU alone does not
        // establish game, NGX runtime, render-pipeline or driver readiness.
        var nr = capabilities.NeuralRendering &&
                 mode == GpuAdaptiveMode.Manual &&
                 manual.NeuralRendering;

        var preset = goal switch
        {
            GpuOptimizationGoal.Quality => "Quality",
            GpuOptimizationGoal.Performance => "Performance",
            _ => "Balanced"
        };

        return new GpuAdaptiveRecommendation(
            mode, goal, preset, sr, fg, reflex, nr,
            capabilities.MultiFrameGeneration && fg,
            "DLSS SR mode is a recommendation only; applying it depends on the game's supported integration. " +
            "FG, MFG and NR also require game/runtime verification. NVIDIA driver and Control Panel settings are never changed.");
    }
}
