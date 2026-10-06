using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed class ReShadeService
{
    private const long MaxInstallerBytes = 256L * 1024 * 1024;
    private const string ManifestUrl =
        "https://raw.githubusercontent.com/ScoopInstaller/Versions/master/bucket/reshade-addons.json";

    private readonly HttpClient _http = new();

    public string CacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "reshade");

    public ReShadeService()
    {
        _http.Timeout = TimeSpan.FromMinutes(3);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
    }

    public async Task<(string Version, string InstallerPath)> GetLatestAddonInstallerAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Checking latest ReShade add-on build…");

        using var response = await _http.GetAsync(
            ManifestUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var version = json.RootElement.GetProperty("version").GetString()
            ?? throw new InvalidOperationException("ReShade version missing from manifest.");
        var url = json.RootElement.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("ReShade download URL missing from manifest.");
        var hash = json.RootElement.GetProperty("hash").GetString()
            ?? throw new InvalidOperationException("ReShade SHA-256 missing from manifest.");

        if (hash.Length != 64 ||
            hash.Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new InvalidDataException(
                "ReShade manifest contains an invalid SHA-256 value.");
        }

        var actualUrl = url.Split('#')[0];
        if (!Uri.TryCreate(actualUrl, UriKind.Absolute, out var downloadUri) ||
            !downloadUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !downloadUri.Host.Equals("reshade.me", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unexpected ReShade installer URL: {actualUrl}");
        }

        Directory.CreateDirectory(CacheDirectory);
        var destination = Path.Combine(
            CacheDirectory,
            $"ReShade_Setup_{version}_Addon.exe");

        if (File.Exists(destination))
        {
            var existing = await HashService.Sha256Async(destination, cancellationToken);
            if (existing.Equals(hash, StringComparison.OrdinalIgnoreCase))
                return (version, destination);

            File.Delete(destination);
        }

        var temp = destination + ".download";
        progress?.Report($"Downloading ReShade {version} with full add-on support…");

        try
        {
            await NetworkRetry.ExecuteAsync(
                async (attempt, token) =>
                {
                    if (attempt > 1)
                    {
                        try
                        {
                            if (File.Exists(temp))
                                File.Delete(temp);
                        }
                        catch { }
                    }

                    using var download = await _http.GetAsync(
                        downloadUri,
                        HttpCompletionOption.ResponseHeadersRead,
                        token);
                    download.EnsureSuccessStatusCode();

                    if (download.Content.Headers.ContentLength is > MaxInstallerBytes)
                    {
                        throw new InvalidDataException(
                            "ReShade installer exceeds the 256 MB safety limit.");
                    }

                    await using (var input =
                        await download.Content.ReadAsStreamAsync(token))
                    await using (var output = new FileStream(
                        temp,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        128 * 1024,
                        useAsync: true))
                    {
                        var buffer = new byte[128 * 1024];
                        long total = 0;

                        while (true)
                        {
                            var read = await input.ReadAsync(
                                buffer.AsMemory(),
                                token);
                            if (read == 0)
                                break;

                            total += read;
                            if (total > MaxInstallerBytes)
                            {
                                throw new InvalidDataException(
                                    "ReShade installer exceeded the 256 MB safety limit.");
                            }

                            await output.WriteAsync(
                                buffer.AsMemory(0, read),
                                token);
                        }
                    }

                    var actualHash =
                        await HashService.Sha256Async(
                            temp,
                            token);

                    if (!actualHash.Equals(
                            hash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"ReShade installer SHA-256 mismatch. Expected {hash}, got {actualHash}.");
                    }
                },
                cancellationToken,
                attempts: 3);

            File.Move(temp, destination, true);
            return (version, destination);
        }
        catch
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch { }

            throw;
        }
    }

    public async Task LaunchAddonInstallerAsync(
        string targetExecutable,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetExecutable) || !File.Exists(targetExecutable))
            throw new FileNotFoundException(
                "The selected game's executable could not be found.",
                targetExecutable);

        var (version, installer) = await GetLatestAddonInstallerAsync(
            progress,
            cancellationToken);

        progress?.Report($"Launching ReShade {version} add-on installer…");

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = installer,
            ArgumentList = { targetExecutable },
            UseShellExecute = true
        }) ?? throw new InvalidOperationException(
            "Windows could not launch the ReShade installer.");
    }


}
