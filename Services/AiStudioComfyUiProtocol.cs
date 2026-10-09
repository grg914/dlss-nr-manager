using System.Text;
using System.Text.Json;

namespace DlssNrManager.Services;

public enum AiStudioComfyUiHistoryState
{
    NotFound,
    Pending,
    Succeeded,
    Failed
}

/// <summary>
/// Bounded, offline parsing for ComfyUI's /prompt and /history/{id} protocol.
/// This is NOT an execution authorization or a model/node trust decision.
/// </summary>
public static class AiStudioComfyUiProtocol
{
    private const int MaxWorkflowBytes = 128 * 1024;
    private const int MaxResponseBytes = 128 * 1024;
    private const int MaxNodes = 64;

    // Deliberately excludes checkpoint/pickle loaders, arbitrary Python,
    // external API nodes and custom nodes until a model-specific workflow is approved.
    private static readonly HashSet<string> ProvisionalCoreNodes = new(StringComparer.Ordinal)
    {
        "EmptyLatentImage", "CLIPTextEncode", "KSampler", "VAEDecode", "SaveImage"
    };

    public static bool TryCreateSubmission(string? workflowJson, out string? submissionJson)
    {
        submissionJson = null;
        if (string.IsNullOrWhiteSpace(workflowJson) ||
            Encoding.UTF8.GetByteCount(workflowJson) > MaxWorkflowBytes)
            return false;

        try
        {
            using var document = JsonDocument.Parse(
                workflowJson,
                new JsonDocumentOptions { MaxDepth = 24 });
            var nodes = document.RootElement;
            if (nodes.ValueKind != JsonValueKind.Object)
                return false;

            var count = 0;
            foreach (var node in nodes.EnumerateObject())
            {
                if (++count > MaxNodes ||
                    node.Name.Length is < 1 or > 10 ||
                    !node.Name.All(char.IsAsciiDigit) ||
                    node.Value.ValueKind != JsonValueKind.Object ||
                    !node.Value.TryGetProperty("class_type", out var type) ||
                    type.ValueKind != JsonValueKind.String ||
                    !ProvisionalCoreNodes.Contains(type.GetString()!) ||
                    !node.Value.TryGetProperty("inputs", out var inputs) ||
                    inputs.ValueKind != JsonValueKind.Object)
                    return false;

                // A SaveImage prefix must never select a path or parent directory.
                if (type.GetString() == "SaveImage" &&
                    inputs.TryGetProperty("filename_prefix", out var prefix) &&
                    (prefix.ValueKind != JsonValueKind.String ||
                     !IsSafeOutputPrefix(prefix.GetString())))
                    return false;

                // Refuse unreviewed top-level node metadata / extensions.
                foreach (var field in node.Value.EnumerateObject())
                    if (field.Name is not ("class_type" or "inputs"))
                        return false;
            }

            if (count == 0)
                return false;

            // Serialize actual parsed JSON, never concatenate untrusted prompt text.
            submissionJson = JsonSerializer.Serialize(new { prompt = nodes });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Constructs a trusted FLUX.2 4B FP8 TEXT-TO-IMAGE API request from
    /// bounded typed inputs, NOT arbitrary user-provided ComfyUI node JSON.
    /// Pure transformation only; it does not submit, launch or approve a GPU
    /// process. The general-purpose untrusted graph policy remains unchanged.
    /// </summary>
    public static bool TryCreateReviewedFlux2TextSubmission(
        string? prompt,
        AiStudioImageSettings? settings,
        out string? submissionJson)
    {
        submissionJson = null;
        if (prompt is null || settings is null)
            return false;

        try
        {
            var graphJson = AiStudioFlux2Fp8WorkflowService.BuildTextToImage(
                prompt, settings);
            if (Encoding.UTF8.GetByteCount(graphJson) > MaxWorkflowBytes)
                return false;

            using var graph = JsonDocument.Parse(
                graphJson, new JsonDocumentOptions { MaxDepth = 24 });
            // Graph is constructed entirely in code with exactly these 13
            // reviewed nodes. No caller-supplied class_type is ever accepted.
            if (graph.RootElement.ValueKind != JsonValueKind.Object ||
                graph.RootElement.EnumerateObject().Count() != 13)
                return false;

            submissionJson = JsonSerializer.Serialize(
                new { prompt = graph.RootElement });
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            return false;
        }
    }

    public static bool TryReadPromptId(string? responseJson, out Guid promptId)
    {
        promptId = default;
        if (!IsBoundedResponse(responseJson))
            return false;

        try
        {
            using var document = JsonDocument.Parse(responseJson!);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("prompt_id", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                Guid.TryParseExact(id.GetString(), "D", out promptId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static AiStudioComfyUiHistoryState ReadHistoryState(
        string? historyJson, Guid promptId)
    {
        if (promptId == Guid.Empty || !IsBoundedResponse(historyJson))
            return AiStudioComfyUiHistoryState.NotFound;

        try
        {
            using var document = JsonDocument.Parse(historyJson!);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(
                    promptId.ToString("D"), out var entry) ||
                entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("status", out var status) ||
                status.ValueKind != JsonValueKind.Object)
                return AiStudioComfyUiHistoryState.NotFound;

            if (!status.TryGetProperty("completed", out var complete) ||
                complete.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !status.TryGetProperty("status_str", out var name) ||
                name.ValueKind != JsonValueKind.String)
                return AiStudioComfyUiHistoryState.Pending;

            var label = name.GetString();
            if (label is "error" or "interrupted")
                return AiStudioComfyUiHistoryState.Failed;

            return complete.GetBoolean() && label == "success"
                ? AiStudioComfyUiHistoryState.Succeeded
                : AiStudioComfyUiHistoryState.Pending;
        }
        catch (JsonException)
        {
            return AiStudioComfyUiHistoryState.NotFound;
        }
    }

    private static bool IsSafeOutputPrefix(string? prefix)
        => prefix is { Length: > 0 and <= 80 } &&
           prefix.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static bool IsBoundedResponse(string? json)
        => !string.IsNullOrWhiteSpace(json) &&
           Encoding.UTF8.GetByteCount(json) <= MaxResponseBytes;
}
