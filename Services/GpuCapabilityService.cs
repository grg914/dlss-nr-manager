using DlssNrManager.Models;

namespace DlssNrManager.Services;

public sealed record RtxCapabilities(
    bool IsSupportedRtx,
    bool SuperResolution,
    bool RayReconstruction,
    bool FrameGeneration,
    bool MultiFrameGeneration,
    bool NeuralRendering,
    string Summary,
    string? BlockingReason = null);

public static class GpuCapabilityService
{
    public static RtxCapabilities Evaluate(GpuInfo gpu)
    {
        if (!gpu.IsNvidia)
        {
            return new(
                false, false, false, false, false, false,
                "Unsupported GPU • NVIDIA GeForce RTX 20/30/40/50 required.",
                "No supported NVIDIA RTX GPU was detected.");
        }

        return gpu.Generation switch
        {
            "RTX 50" => new(
                true, true, true, true, true, true,
                "RTX 50 • DLSS SR + Ray Reconstruction + Frame Generation + Multi Frame Generation + Neural Rendering"),
            "RTX 40" => new(
                true, true, true, true, false, false,
                "RTX 40 • DLSS SR + Ray Reconstruction + Frame Generation • Neural Rendering/MFG unavailable"),
            "RTX 30" => new(
                true, true, true, false, false, false,
                "RTX 30 • DLSS SR + Ray Reconstruction • Frame Generation/Neural Rendering unavailable"),
            "RTX 20" => new(
                true, true, true, false, false, false,
                "RTX 20 • DLSS SR + Ray Reconstruction • Frame Generation/Neural Rendering unavailable"),
            _ => new(
                false, false, false, false, false, false,
                $"{gpu.Name} • NVIDIA detected but not a supported GeForce RTX 20/30/40/50 GPU",
                "This NVIDIA GPU is not recognized as a supported GeForce RTX generation.")
        };
    }
}
