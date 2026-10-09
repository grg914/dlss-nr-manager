using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiStudioComfyUiEndpointPolicyTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8188/")]
    [InlineData("http://127.0.0.1:8188")]
    [InlineData("http://[::1]:8188/")]
    public void Allows_only_explicit_numeric_loopback_http_root(string value)
    {
        Assert.True(AiStudioComfyUiEndpointPolicy.TryValidate(value, out var endpoint));
        Assert.NotNull(endpoint);
        Assert.Equal("http", endpoint.Scheme);
        Assert.Equal(8188, endpoint.Port);
        Assert.Equal("/", endpoint.AbsolutePath);
    }

    [Theory]
    [InlineData("http://localhost:8188/")]
    [InlineData("http://comfyui.local:8188/")]
    [InlineData("http://example.com:8188/")]
    [InlineData("http://192.168.1.10:8188/")]
    [InlineData("http://0.0.0.0:8188/")]
    [InlineData("http://127.0.0.2:8188/")]
    [InlineData("https://127.0.0.1:8188/")]
    [InlineData("file:///C:/temp/")]
    [InlineData("ws://127.0.0.1:8188/")]
    [InlineData("http://127.0.0.1:80/")]
    [InlineData("http://127.0.0.1:8188/api")]
    [InlineData("http://127.0.0.1:8188/?token=secret")]
    [InlineData("http://127.0.0.1:8188/#fragment")]
    [InlineData("http://user:pass@127.0.0.1:8188/")]
    [InlineData("http://127.0.0.1:8188/\\@example.com")]
    [InlineData("http://[::2]:8188/")]
    [InlineData("127.0.0.1:8188")]
    [InlineData(" http://127.0.0.1:8188/")]
    [InlineData("http://127.0.0.1:8188/\n")]
    public void Rejects_external_ambiguous_or_overprivileged_addresses(string value)
    {
        Assert.False(AiStudioComfyUiEndpointPolicy.TryValidate(value, out var endpoint));
        Assert.Null(endpoint);
    }

    [Fact]
    public void Rejects_missing_configuration_without_attempting_network_access()
    {
        Assert.False(AiStudioComfyUiEndpointPolicy.TryValidate(null, out var endpoint));
        Assert.Null(endpoint);
    }
}
