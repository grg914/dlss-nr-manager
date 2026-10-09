using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiStudioOfficialComfyUiDownloadTests
{
    private const string OfficialUrl =
        "https://github.com/Comfy-Org/ComfyUI/releases/download/v0.39.0/ComfyUI_windows_portable_nvidia.7z";

    [Fact]
    public void Only_official_stable_nvidia_asset_with_digest_is_offered()
    {
        using var verified = JsonDocument.Parse(ReleaseMetadata(
            OfficialUrl, "sha256:" + new string('a', 64)));
        Assert.True(AiStudioOfficialComfyUiDownloadService.TryReadAsset(
            verified.RootElement, out var asset));
        Assert.Equal(1234, asset!.Size);
        Assert.Equal("v0.39.0", asset.Tag);

        using var wrongHost = JsonDocument.Parse(ReleaseMetadata(
            OfficialUrl.Replace("github.com", "example.com"), "sha256:" + new string('a', 64)));
        Assert.False(AiStudioOfficialComfyUiDownloadService.TryReadAsset(wrongHost.RootElement, out _));

        using var missingDigest = JsonDocument.Parse(ReleaseMetadata(OfficialUrl, "sha256:bad"));
        Assert.False(AiStudioOfficialComfyUiDownloadService.TryReadAsset(missingDigest.RootElement, out _));

        using var prerelease = JsonDocument.Parse(
            ReleaseMetadata(OfficialUrl, "sha256:" + new string('a', 64))
                .Replace("v0.39.0", "v0.40.0-rc1"));
        Assert.False(AiStudioOfficialComfyUiDownloadService.TryReadAsset(prerelease.RootElement, out _));
    }

    [Fact]
    public async Task Downloads_exact_official_bytes_to_staging_without_installation()
    {
        using var fixture = new Fixture();
        var data = Encoding.ASCII.GetBytes("fake portable archive for isolated test");
        var sha = Convert.ToHexString(SHA256.HashData(data));
        var asset = new AiStudioOfficialComfyUiAsset("v0.39.0", OfficialUrl, data.Length, sha);
        using var service = new AiStudioOfficialComfyUiDownloadService(
            fixture.Root,
            new FakeHandler(request =>
            {
                Assert.Equal(OfficialUrl, request.RequestUri!.AbsoluteUri);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(data)
                };
            }));

        var path = await service.StageAsync(asset);
        Assert.Equal(data, File.ReadAllBytes(path));
        Assert.Contains(Path.Combine("downloads", "official-comfyui", "v0.39.0"), path);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "runtime", "comfyui")));
    }

    [Fact]
    public async Task Bad_sha_does_not_promote_or_leave_partial_download()
    {
        using var fixture = new Fixture();
        var asset = new AiStudioOfficialComfyUiAsset(
            "v0.39.0", OfficialUrl, 4, new string('b', 64));
        using var service = new AiStudioOfficialComfyUiDownloadService(
            fixture.Root, new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes("test"))
            }));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageAsync(asset));
        var staging = Path.Combine(fixture.Root, "downloads", "official-comfyui", "v0.39.0");
        Assert.Empty(Directory.GetFiles(staging));
    }

    [Fact]
    public async Task A_redirect_outside_official_git_hosts_is_rejected()
    {
        using var fixture = new Fixture();
        var asset = new AiStudioOfficialComfyUiAsset(
            "v0.39.0", OfficialUrl, 4, new string('a', 64));
        using var service = new AiStudioOfficialComfyUiDownloadService(
            fixture.Root, new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.com/evil.7z") }
            }));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageAsync(asset));
    }

    [Fact]
    public async Task Existing_staging_archive_is_never_overwritten()
    {
        using var fixture = new Fixture();
        var destination = Path.Combine(
            fixture.Root, "downloads", "official-comfyui", "v0.39.0",
            "ComfyUI_windows_portable_nvidia.7z");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "old");
        var asset = new AiStudioOfficialComfyUiAsset(
            "v0.39.0", OfficialUrl, 3, new string('a', 64));
        using var service = new AiStudioOfficialComfyUiDownloadService(
            fixture.Root, new FakeHandler(_ => throw new Exception("network must not be used")));

        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(asset));
        Assert.Equal("old", File.ReadAllText(destination));
    }

    private static string ReleaseMetadata(string url, string digest)
        => JsonSerializer.Serialize(new
        {
            tag_name = "v0.39.0",
            assets = new[]
            {
                new
                {
                    name = "ComfyUI_windows_portable_nvidia.7z",
                    browser_download_url = url,
                    size = 1234,
                    digest
                }
            }
        });

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(reply(request));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "comfy-official-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
