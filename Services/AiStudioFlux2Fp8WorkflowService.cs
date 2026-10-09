using System.Text.Json;

namespace DlssNrManager.Services;

/// <summary>
/// Bounded ComfyUI API graph construction from the reviewed FLUX.2 Klein 4B
/// FP8 component names. The upstream UI JSON is a reference, not an API graph.
/// Pure/offline: DOES NOT submit jobs, start Python, or authorize inference.
/// </summary>
public sealed record AiStudioImageSettings(
    int Width, int Height, int Steps, long Seed)
{
    public void Validate()
    {
        if (Width is < 256 or > 2048 ||
            Height is < 256 or > 2048 ||
            Width % 16 != 0 || Height % 16 != 0 ||
            (long)Width * Height > 2048L * 2048 ||
            Steps is < 1 or > 32 ||
            Seed is < 0 or > 1_125_899_906_842_623)
            throw new ArgumentOutOfRangeException(
                nameof(Width), "Unsupported FLUX.2 FP8 generation settings.");
    }
}

public static class AiStudioFlux2Fp8WorkflowService
{
    public const string ModelName = "flux-2-klein-4b-fp8.safetensors";
    public const string EncoderName = "qwen_3_4b.safetensors";
    public const string VaeName = "flux2-vae.safetensors";

    public static string BuildTextToImage(
        string prompt, int width = 1024, int height = 1024,
        long seed = 42, int steps = 4)
        => Build(prompt, null, new AiStudioImageSettings(width, height, steps, seed));

    public static string BuildTextToImage(string prompt, AiStudioImageSettings settings)
        => Build(prompt, null, settings);

    public static string BuildImageEdit(
        string prompt, string importedImageName,
        int width = 1024, int height = 1024, long seed = 42, int steps = 4)
    {
        if (string.IsNullOrWhiteSpace(importedImageName) ||
            importedImageName.Length > 120 ||
            importedImageName != Path.GetFileName(importedImageName) ||
            !importedImageName.All(c => char.IsAsciiLetterOrDigit(c) ||
                                       c is '.' or '_' or '-') ||
            !new[] { ".png", ".jpg", ".jpeg", ".webp" }
                .Contains(Path.GetExtension(importedImageName), StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Image edits require an approved, already-imported ComfyUI input filename.",
                nameof(importedImageName));
        }
        return Build(prompt, importedImageName,
            new AiStudioImageSettings(width, height, steps, seed));
    }

    private static string Build(
        string prompt, string? inputName, AiStudioImageSettings settings)
    {
        if (string.IsNullOrWhiteSpace(prompt) ||
            prompt.Length > 2048 ||
            prompt.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
            throw new ArgumentException("Prompt is empty or out of bounds.", nameof(prompt));
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var (width, height, steps, seed) =
            (settings.Width, settings.Height, settings.Steps, settings.Seed);

        static object[] Ref(string id) => [id, 0];
        static object Node(string name, object inputs) => new { class_type = name, inputs };

        var nodes = new Dictionary<string, object>
        {
            ["1"] = Node("UNETLoader", new { unet_name = ModelName, weight_dtype = "default" }),
            ["2"] = Node("CLIPLoader", new { clip_name = EncoderName, type = "flux2", device = "default" }),
            ["3"] = Node("VAELoader", new { vae_name = VaeName }),
            ["4"] = Node("CLIPTextEncode", new { clip = Ref("2"), text = prompt }),
            ["5"] = Node("ConditioningZeroOut", new { conditioning = Ref("4") }),
            ["6"] = Node("EmptyFlux2LatentImage", new { width, height, batch_size = 1 }),
            ["7"] = Node("Flux2Scheduler", new { steps, width, height }),
            ["8"] = Node("RandomNoise", new { noise_seed = seed }),
            ["9"] = Node("KSamplerSelect", new { sampler_name = "euler" }),
            ["10"] = Node("CFGGuider", new
            {
                model = Ref("1"), positive = Ref("4"),
                negative = Ref("5"), cfg = 1
            }),
            ["11"] = Node("SamplerCustomAdvanced", new
            {
                noise = Ref("8"), guider = Ref("10"), sampler = Ref("9"),
                sigmas = Ref("7"), latent_image = Ref("6")
            }),
            ["12"] = Node("VAEDecode", new { samples = Ref("11"), vae = Ref("3") }),
            ["13"] = Node("SaveImage", new
            {
                filename_prefix = "AIStudio_FLUX2",
                images = Ref("12")
            })
        };

        if (inputName is not null)
        {
            // Match ComfyUI's core ReferenceLatent conditioning scheme from
            // its official distilled FP8 image-editing UI workflow.
            nodes["14"] = Node("LoadImage", new { image = inputName });
            nodes["15"] = Node("VAEEncode", new { pixels = Ref("14"), vae = Ref("3") });
            nodes["16"] = Node("ReferenceLatent", new
            {
                conditioning = Ref("4"), latent = Ref("15")
            });
            nodes["17"] = Node("ReferenceLatent", new
            {
                conditioning = Ref("5"), latent = Ref("15")
            });
            nodes["10"] = Node("CFGGuider", new
            {
                model = Ref("1"), positive = Ref("16"),
                negative = Ref("17"), cfg = 1
            });
        }

        // Serializer encodes user-supplied prompt/image as data, never as JSON syntax.
        return JsonSerializer.Serialize(nodes);
    }
}
