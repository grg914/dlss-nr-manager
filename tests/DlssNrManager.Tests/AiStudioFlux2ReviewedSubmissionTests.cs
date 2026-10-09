using System.Text.Json;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiStudioFlux2ReviewedSubmissionTests
{
    [Fact]
    public void Generates_exact_pinned_flux2_text_graph_with_json_escaped_prompt()
    {
        var settings = new AiStudioImageSettings(1024, 576, 8, 1234);
        var prompt = "A fjord \"photo\" \\n with an image";
        Assert.True(AiStudioComfyUiProtocol.TryCreateReviewedFlux2TextSubmission(
            prompt, settings, out var payload));

        using var json = JsonDocument.Parse(payload!);
        var graph = json.RootElement.GetProperty("prompt");
        Assert.Equal(13, graph.EnumerateObject().Count());
        Assert.Equal("UNETLoader",
            graph.GetProperty("1").GetProperty("class_type").GetString());
        Assert.Equal("flux-2-klein-4b-fp8.safetensors",
            graph.GetProperty("1").GetProperty("inputs").GetProperty("unet_name").GetString());
        Assert.Equal("CLIPLoader",
            graph.GetProperty("2").GetProperty("class_type").GetString());
        Assert.Equal("qwen_3_4b.safetensors",
            graph.GetProperty("2").GetProperty("inputs").GetProperty("clip_name").GetString());
        Assert.Equal(prompt,
            graph.GetProperty("4").GetProperty("inputs").GetProperty("text").GetString());
        Assert.Equal(1024,
            graph.GetProperty("6").GetProperty("inputs").GetProperty("width").GetInt32());
        Assert.Equal(576,
            graph.GetProperty("6").GetProperty("inputs").GetProperty("height").GetInt32());
        Assert.Equal(8,
            graph.GetProperty("7").GetProperty("inputs").GetProperty("steps").GetInt32());
        Assert.Equal("SaveImage",
            graph.GetProperty("13").GetProperty("class_type").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_empty_prompts(string prompt)
    {
        Assert.False(AiStudioComfyUiProtocol.TryCreateReviewedFlux2TextSubmission(
            prompt, new AiStudioImageSettings(1024, 1024, 4, 1), out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void Refuses_null_settings_and_unreviewed_steps()
    {
        Assert.False(AiStudioComfyUiProtocol.TryCreateReviewedFlux2TextSubmission(
            "Hello", null, out var missing));
        Assert.Null(missing);

        Assert.False(AiStudioComfyUiProtocol.TryCreateReviewedFlux2TextSubmission(
            "Hello", new AiStudioImageSettings(1024, 1024, 1000, 1), out var invalid));
        Assert.Null(invalid);
    }

    [Fact]
    public void Legacy_arbitrary_graph_restrictions_are_not_weakened()
    {
        var workflow = AiStudioFlux2Fp8WorkflowService.BuildTextToImage(
            "A portrait", new AiStudioImageSettings(768, 768, 4, 11));
        Assert.False(AiStudioComfyUiProtocol.TryCreateSubmission(
            workflow, out var unexpected));
        Assert.Null(unexpected);
    }
}
