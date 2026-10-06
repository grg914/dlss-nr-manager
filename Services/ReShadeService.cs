using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed class ReShadeService
{
    private const string ManifestUrl =
        "https://raw.githubusercontent.com/ScoopInstaller/Versions/master/bucket/reshade-addons.json";

    private readonly HttpClient _http = new();

    public string CacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "reshade");

    public ReShadeService()
    {
        _http.Timeout = TimeSpan.FromSeconds(30);
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
            var existing = await Sha256Async(destination, cancellationToken);
            if (existing.Equals(hash, StringComparison.OrdinalIgnoreCase))
                return (version, destination);

            File.Delete(destination);
        }

        var temp = destination + ".download";
        progress?.Report($"Downloading ReShade {version} with full add-on support…");

        try
        {
            using var download = await _http.GetAsync(
                downloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            download.EnsureSuccessStatusCode();

            await using (var input = await download.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                             temp,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            var actualHash = await Sha256Async(temp, cancellationToken);
            if (!actualHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"ReShade installer SHA-256 mismatch. Expected {hash}, got {actualHash}.");
            }

            File.Move(temp, destination, true);
            return (version, destination);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
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

        Process.Start(new ProcessStartInfo
        {
            FileName = installer,
            Arguments = $"\"{targetExecutable}\"",
            UseShellExecute = true
        });
    }

    private static async Task<string> Sha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
