using DlssNrManager.Models;

namespace DlssNrManager.Services;

/// <summary>
/// One fail-closed selection shared by ordinary Install, manual staging and
/// the game preset. UI checkboxes are requests, never capability grants.
/// </summary>
public static class GameNvidiaSelectionPolicy
{
    public static GenericNvidiaFeatureSelection Effective(
        GpuInfo gpu,
        HardwareOperatingMode mode,
        GenericNvidiaFeatureSelection requested)
    {
        var caps = GpuCapabilityService.Evaluate(gpu);
        var compatible = mode == HardwareOperatingMode.Compatible;
        return new GenericNvidiaFeatureSelection(
            requested.SuperResolution && caps.SuperResolution,
            requested.FrameGeneration && caps.FrameGeneration && !compatible,
            requested.Reflex && caps.IsSupportedRtx,
            requested.NeuralRendering && caps.NeuralRendering && !compatible);
    }
}
