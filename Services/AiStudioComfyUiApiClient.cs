using System.Net.Http;
using System.Text;

namespace DlssNrManager.Services;

/// <summary>
/// Minimal loopback-only ComfyUI transport. Not wired to production Jobs:
/// runtime provenance, model licenses, process ownership and workflow approvals
/// must be enforced by a future explicit executor before invoking submission.
/// </summary>
public sealed class AiStudioComfyUiApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    public AiStudioComfyUiApiClient(string endpoint, HttpMessageHandler? handler = null)
    {
        if (!AiStudioComfyUiEndpointPolicy.TryValidate(endpoint, out var validated))
            throw new ArgumentException("Numeric local ComfyUI endpoint required.", nameof(endpoint));

        _endpoint = validated!;
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            UseProxy = false,
            UseCookies = false,
            AllowAutoRedirect = false
        }, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<Guid> SubmitReviewedWorkflowAsync(
        string workflowJson,
        CancellationToken cancellationToken = default)
    {
        if (!AiStudioComfyUiProtocol.TryCreateSubmission(workflowJson, out var body))
            throw new ArgumentException("Unreviewed or invalid ComfyUI workflow.", nameof(workflowJson));

        var requestUri = new Uri(_endpoint, "prompt");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(body!, Encoding.UTF8, "application/json")
        };

        var responseJson = await SendAndReadAsync(request, 16 * 1024, cancellationToken);
        if (!AiStudioComfyUiProtocol.TryReadPromptId(responseJson, out var promptId))
            throw new InvalidOperationException("ComfyUI returned no valid prompt ID.");

        return promptId;
    }

    public async Task<AiStudioComfyUiHistoryState> ReadHistoryAsync(
        Guid promptId,
        CancellationToken cancellationToken = default)
    {
        if (promptId == Guid.Empty)
            throw new ArgumentException("Prompt ID required.", nameof(promptId));

        var requestUri = new Uri(_endpoint, "history/" + promptId.ToString("D"));
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var json = await SendAndReadAsync(request, 128 * 1024, cancellationToken);
        return AiStudioComfyUiProtocol.ReadHistoryState(json, promptId);
    }

    private async Task<string> SendAndReadAsync(
        HttpRequestMessage request,
        int limit,
        CancellationToken cancellationToken)
    {
        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // Reject redirect responses and any transport that silently changed
        // the request destination (including a custom injected handler).
        if (!response.IsSuccessStatusCode ||
            response.RequestMessage?.RequestUri is not { } actual ||
            actual != request.RequestUri)
            throw new InvalidOperationException("Untrusted ComfyUI HTTP response.");

        if (response.Content.Headers.ContentLength is long size && size > limit)
            throw new InvalidOperationException("Oversized ComfyUI response.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + count > limit)
                throw new InvalidOperationException("Oversized ComfyUI response.");
            memory.Write(buffer, 0, count);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }

    public void Dispose() => _http.Dispose();
}
