using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiStudioComfyUiApiClientTests
{
    private const string SimpleWorkflow =
        """{"1":{"class_type":"EmptyLatentImage","inputs":{"width":512,"height":512}},"2":{"class_type":"SaveImage","inputs":{"filename_prefix":"AIStudio","images":["1",0]}}}""";

    [Fact]
    public void Creates_offline_envelope_only_for_bounded_core_nodes()
    {
        Assert.True(AiStudioComfyUiProtocol.TryCreateSubmission(SimpleWorkflow, out var body));
        using var document = JsonDocument.Parse(body!);
        Assert.Equal("EmptyLatentImage", document.RootElement
            .GetProperty("prompt").GetProperty("1").GetProperty("class_type").GetString());

        Assert.False(AiStudioComfyUiProtocol.TryCreateSubmission(
            """{"1":{"class_type":"CustomPythonNode","inputs":{}}}""", out _));
        Assert.False(AiStudioComfyUiProtocol.TryCreateSubmission(
            """{"1":{"class_type":"CheckpointLoaderSimple","inputs":{"ckpt_name":"untrusted.ckpt"}}}""", out _));
        Assert.False(AiStudioComfyUiProtocol.TryCreateSubmission(
            """{"1":{"class_type":"SaveImage","inputs":{"filename_prefix":"../../outside"}}}""", out _));
        Assert.False(AiStudioComfyUiProtocol.TryCreateSubmission("{}", out _));
        Assert.False(AiStudioComfyUiProtocol.TryCreateSubmission("{bad json", out _));
        Assert.False(AiStudioComfyUiProtocol.TryCreateSubmission(new string('x', 140_000), out _));
    }

    [Fact]
    public void History_must_explicitly_report_completed_success()
    {
        var id = Guid.NewGuid();
        var key = id.ToString("D");
        Assert.Equal(AiStudioComfyUiHistoryState.NotFound,
            AiStudioComfyUiProtocol.ReadHistoryState("{}", id));
        Assert.Equal(AiStudioComfyUiHistoryState.Pending,
            AiStudioComfyUiProtocol.ReadHistoryState(
                "{\"" + key + "\":{\"status\":{\"completed\":false,\"status_str\":\"success\"}}}", id));
        Assert.Equal(AiStudioComfyUiHistoryState.Succeeded,
            AiStudioComfyUiProtocol.ReadHistoryState(
                "{\"" + key + "\":{\"status\":{\"completed\":true,\"status_str\":\"success\"}}}", id));
        Assert.Equal(AiStudioComfyUiHistoryState.Failed,
            AiStudioComfyUiProtocol.ReadHistoryState(
                "{\"" + key + "\":{\"status\":{\"completed\":true,\"status_str\":\"error\"}}}", id));
    }

    [Fact]
    public void Rejects_network_or_proxy_destinations()
    {
        Assert.Throws<ArgumentException>(() =>
            new AiStudioComfyUiApiClient("http://example.com:8188/"));
        Assert.Throws<ArgumentException>(() =>
            new AiStudioComfyUiApiClient("http://localhost:8188/"));
    }

    [Fact]
    public async Task Submits_offline_reviewed_shape_to_local_prompt_only()
    {
        var id = Guid.NewGuid();
        var mock = new LocalHandler(_ =>
            (HttpStatusCode.OK, "{\"prompt_id\":\"" + id.ToString("D") + "\"}"));
        using var client = new AiStudioComfyUiApiClient("http://127.0.0.1:8188/", mock);

        Assert.Equal(id, await client.SubmitReviewedWorkflowAsync(SimpleWorkflow));
        Assert.Equal("http://127.0.0.1:8188/prompt", mock.RequestUri);
        Assert.Equal(HttpMethod.Post, mock.Method);
        Assert.Contains("\"prompt\"", mock.RequestBody);
        Assert.Equal(1, mock.Count);
    }

    [Fact]
    public async Task Unreviewed_nodes_never_issue_http()
    {
        var mock = new LocalHandler(_ => (HttpStatusCode.OK, "{}"));
        using var client = new AiStudioComfyUiApiClient("http://127.0.0.1:8188/", mock);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SubmitReviewedWorkflowAsync(
                """{"1":{"class_type":"UntrustedCustomNode","inputs":{}}}"""));
        Assert.Equal(0, mock.Count);
    }

    [Fact]
    public async Task Reads_only_the_requested_history_entry()
    {
        var id = Guid.NewGuid();
        var body = "{\"" + id.ToString("D") +
                   "\":{\"status\":{\"completed\":true,\"status_str\":\"success\"}}}";
        var mock = new LocalHandler(_ => (HttpStatusCode.OK, body));
        using var client = new AiStudioComfyUiApiClient("http://[::1]:8188/", mock);
        Assert.Equal(AiStudioComfyUiHistoryState.Succeeded, await client.ReadHistoryAsync(id));
        Assert.Equal("http://[::1]:8188/history/" + id.ToString("D"), mock.RequestUri);
    }

    [Fact]
    public async Task Redirects_errors_and_oversized_responses_are_rejected()
    {
        foreach (var response in new[]
        {
            (HttpStatusCode.Redirect, ""),
            (HttpStatusCode.BadRequest, "{}"),
            (HttpStatusCode.OK, new string('a', 17_000))
        })
        {
            var mock = new LocalHandler(_ => response);
            using var client = new AiStudioComfyUiApiClient("http://127.0.0.1:8188/", mock);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.SubmitReviewedWorkflowAsync(SimpleWorkflow));
        }
    }

    private sealed class LocalHandler(
        Func<HttpRequestMessage, (HttpStatusCode status, string body)> responder)
        : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            RequestUri = request.RequestUri?.ToString();
            Method = request.Method;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var (status, body) = responder(request);
            return new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
