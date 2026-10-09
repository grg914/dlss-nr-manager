using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record AiStudioOfficialComfyUiAsset(
    string Tag,
    string Url,
    long Size,
    string Sha256);

/// <summary>
/// Explicit acquisition of the official NVIDIA portable ComfyUI archive.
/// Downloads into private STAGING only; never installs, launches or promotes it.
/// </summary>
public sealed class AiStudioOfficialComfyUiDownloadService : IDisposable
{
    private const string AssetName = "ComfyUI_windows_portable_nvidia.7z";
    private const long MaxAssetBytes = 1950L * 1024 * 1024;
    private const string ApiUrl = "https://api.github.com/repos/Comfy-Org/ComfyUI/releases/latest";
    private readonly HttpClient _http;
    private readonly string _stagingRoot;

    public AiStudioOfficialComfyUiDownloadService(
        string aiStudioRoot,
        HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aiStudioRoot);
        _stagingRoot = Path.Combine(Path.GetFullPath(aiStudioRoot), "downloads", "official-comfyui");
        _http = new HttpClient(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false
        }, disposeHandler: true)
        {
            Timeout = TimeSpan.FromHours(4)
        };
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<AiStudioOfficialComfyUiAsset?> FindLatestAsync(
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 1024 * 1024)
            throw new InvalidDataException("Oversized ComfyUI release metadata.");

        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(bytes, deadline.Token)) > 0)
        {
            if (buffer.Length + count > 1024 * 1024)
                throw new InvalidDataException("Oversized ComfyUI release metadata.");
            buffer.Write(bytes, 0, count);
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return TryReadAsset(document.RootElement, out var asset) ? asset : null;
    }

    public static bool TryReadAsset(JsonElement release, out AiStudioOfficialComfyUiAsset? asset)
    {
        asset = null;
        if (release.ValueKind != JsonValueKind.Object ||
            !release.TryGetProperty("tag_name", out var tagElement) ||
            tagElement.ValueKind != JsonValueKind.String ||
            !IsStableTag(tagElement.GetString()) ||
            !release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
            return false;

        var tag = tagElement.GetString()!;
        var expectedUrl =
            $"https://github.com/Comfy-Org/ComfyUI/releases/download/{tag}/{AssetName}";

        foreach (var item in assets.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("name", out var name) ||
                name.ValueKind != JsonValueKind.String ||
                name.GetString() != AssetName)
                continue;

            if (!item.TryGetProperty("browser_download_url", out var url) ||
                url.ValueKind != JsonValueKind.String ||
                url.GetString() != expectedUrl ||
                !item.TryGetProperty("size", out var size) ||
                size.ValueKind != JsonValueKind.Number ||
                !size.TryGetInt64(out var length) ||
                length is <= 0 or > MaxAssetBytes ||
                !item.TryGetProperty("digest", out var digest) ||
                digest.ValueKind != JsonValueKind.String)
                return false;

            var value = digest.GetString();
            if (value is not { Length: 71 } ||
                !value.StartsWith("sha256:", StringComparison.Ordinal) ||
                !value.AsSpan(7).ToString().All(Uri.IsHexDigit))
                return false;

            asset = new AiStudioOfficialComfyUiAsset(
                tag, expectedUrl, length, value[7..].ToLowerInvariant());
            return true;
        }
        return false;
    }

    public async Task<string> StageAsync(
        AiStudioOfficialComfyUiAsset asset,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!IsStableTag(asset.Tag) ||
            asset.Url != $"https://github.com/Comfy-Org/ComfyUI/releases/download/{asset.Tag}/{AssetName}" ||
            asset.Size is <= 0 or > MaxAssetBytes ||
            asset.Sha256 is not { Length: 64 } ||
            !asset.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Untrusted official ComfyUI asset metadata.");

        cancellationToken.ThrowIfCancellationRequested();

        var destinationDir = Path.Combine(_stagingRoot, asset.Tag);
        var destination = Path.Combine(destinationDir, AssetName);
        if (ManagedPathSafety.HasReparsePointOnPath(destination))
            throw new IOException("ComfyUI staging path is redirected.");

        Directory.CreateDirectory(destinationDir);

        if (File.Exists(destination))
        {
            if (new FileInfo(destination).Length == asset.Size &&
                string.Equals(
                    await HashService.Sha256Async(destination, cancellationToken),
                    asset.Sha256, StringComparison.OrdinalIgnoreCase))
                return destination;
            throw new IOException("An existing ComfyUI staging archive has a different hash. No file was replaced.");
        }

        await LargeDownloadApprovalHub.EnsureApprovedAsync(
            "ComfyUI NVIDIA portable " + asset.Tag,
            asset.Size,
            "Download the official GitHub archive to AI Studio staging. No install or execution.",
            cancellationToken);

        var temporary = Path.Combine(
            destinationDir, ".download-" + Guid.NewGuid().ToString("N") + ".part");
        using var transfer = DownloadProgressHub.Begin(
            "Official ComfyUI " + asset.Tag, asset.Size);

        try
        {
            progress?.Report("Downloading official ComfyUI archive (staging only)…");
            using var response = await GetOfficialAssetAsync(new Uri(asset.Url), cancellationToken);
            if (response.Content.Headers.ContentLength is long advertised &&
                advertised != asset.Size)
                throw new InvalidDataException("ComfyUI asset length does not match GitHub metadata.");

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                downloaded += read;
                if (downloaded > asset.Size)
                    throw new InvalidDataException("Official ComfyUI download exceeded its expected size.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                transfer.Report(downloaded);
            }

            if (downloaded != asset.Size ||
                !string.Equals(
                    Convert.ToHexString(hash.GetHashAndReset()),
                    asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Official ComfyUI download SHA-256/size mismatch.");

            await output.FlushAsync(cancellationToken);
            output.Close();
            cancellationToken.ThrowIfCancellationRequested();
            if (ManagedPathSafety.HasReparsePointOnPath(destination))
                throw new IOException("ComfyUI staging directory was redirected during transfer.");
            File.Move(temporary, destination); // Never overwrite a previous valid download.
            transfer.Complete();
            progress?.Report("Official archive SHA-256 verified and staged; NOT installed or activated.");
            return destination;
        }
        catch (Exception ex)
        {
            transfer.Fail(ex);
            throw;
        }
        finally
        {
            if (!ManagedPathSafety.HasReparsePointOnPath(temporary) && File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private async Task<HttpResponseMessage> GetOfficialAssetAsync(
        Uri current,
        CancellationToken cancellationToken)
    {
        for (var hops = 0; hops < 5; hops++)
        {
            if (!IsTrustedDownloadHost(current))
                throw new InvalidDataException("ComfyUI redirect left official GitHub asset hosts.");

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Found or
                HttpStatusCode.MovedPermanently or
                HttpStatusCode.TemporaryRedirect or
                HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther)
            {
                var next = response.Headers.Location;
                response.Dispose();
                if (next is null)
                    throw new InvalidDataException("ComfyUI release redirect has no destination.");
                current = next.IsAbsoluteUri ? next : new Uri(current, next);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                try { response.EnsureSuccessStatusCode(); }
                finally { response.Dispose(); }
            }
            return response;
        }

        throw new InvalidDataException("Too many ComfyUI download redirects.");
    }

    private static bool IsTrustedDownloadHost(Uri url)
        => url.Scheme == Uri.UriSchemeHttps &&
           url.IsDefaultPort &&
           string.IsNullOrEmpty(url.UserInfo) &&
           (url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            url.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
            url.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsStableTag(string? tag)
        => tag is { Length: > 2 and < 30 } &&
           tag[0] == 'v' &&
           tag[1..].All(c => char.IsAsciiDigit(c) || c == '.') &&
           Version.TryParse(tag[1..], out var version) &&
           version is { Build: >= 0 };

    public void Dispose() => _http.Dispose();
}
