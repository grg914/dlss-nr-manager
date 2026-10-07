using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed class ReShadeService
{
    private const long MaxInstallerBytes = 256L * 1024 * 1024;
    private const long MaxPackageBytes = 384L * 1024 * 1024;
    private const string ManagerLatestReleaseApi =
        "https://api.github.com/repos/grg914/dlss-nr-manager/releases/latest";
    private const string AssetPrefix = "ReShade-Setup-";
    private const string AssetSuffix = "-vendored.zip";

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
        progress?.Report("Checking manager-owned ReShade add-on build…");

        using var response = await _http.GetAsync(
            ManagerLatestReleaseApi,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!json.RootElement.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Manager release does not contain an assets collection.");
        }

        string? assetName = null;
        string? assetUrl = null;
        string? digest = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name) ||
                !name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            assetName = name;
            assetUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;
            digest = asset.TryGetProperty("digest", out var digestElement)
                ? digestElement.GetString()
                : null;
            break;
        }

        if (string.IsNullOrWhiteSpace(assetName) ||
            string.IsNullOrWhiteSpace(assetUrl))
        {
            throw new InvalidOperationException(
                "Latest DLSS NR Manager release has no vendored ReShade setup asset.");
        }

        var versionLength =
            assetName.Length - AssetPrefix.Length - AssetSuffix.Length;
        if (versionLength <= 0)
        {
            throw new InvalidDataException(
                $"Unexpected manager-owned ReShade asset name: {assetName}");
        }

        var version = assetName.Substring(
            AssetPrefix.Length,
            versionLength);

        string? expectedHash = null;
        if (!string.IsNullOrWhiteSpace(digest) &&
            digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            expectedHash = digest["sha256:".Length..];
        }

        if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var downloadUri) ||
            !downloadUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !downloadUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unexpected manager-owned ReShade package URL: {assetUrl}");
        }

        Directory.CreateDirectory(CacheDirectory);
        var package = Path.Combine(
            CacheDirectory,
            assetName);
        var installer = Path.Combine(
            CacheDirectory,
            $"ReShade_Setup_{version}_Addon.exe");

        if (File.Exists(package) && !string.IsNullOrWhiteSpace(expectedHash))
        {
            var existingHash = await HashService.Sha256Async(
                package,
                cancellationToken);
            if (!existingHash.Equals(
                    expectedHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(package);
                if (File.Exists(installer))
                    File.Delete(installer);
            }
        }

        if (!File.Exists(package))
        {
            var temp = package + ".download";
            progress?.Report(
                $"Downloading manager-owned ReShade {version} package…");

            try
            {
                await NetworkRetry.ExecuteAsync(
                    async (attempt, token) =>
                    {
                        if (attempt > 1 && File.Exists(temp))
                            File.Delete(temp);

                        using var download = await _http.GetAsync(
                            downloadUri,
                            HttpCompletionOption.ResponseHeadersRead,
                            token);
                        download.EnsureSuccessStatusCode();

                        if (download.Content.Headers.ContentLength is > MaxPackageBytes)
                        {
                            throw new InvalidDataException(
                                "ReShade package exceeds the 384 MB safety limit.");
                        }

                        await using var input =
                            await download.Content.ReadAsStreamAsync(token);
                        await using var output = new FileStream(
                            temp,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            128 * 1024,
                            useAsync: true);

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
                            if (total > MaxPackageBytes)
                            {
                                throw new InvalidDataException(
                                    "ReShade package exceeded the 384 MB safety limit.");
                            }

                            await output.WriteAsync(
                                buffer.AsMemory(0, read),
                                token);
                        }

                        if (!string.IsNullOrWhiteSpace(expectedHash))
                        {
                            var actualHash =
                                await HashService.Sha256Async(
                                    temp,
                                    token);
                            if (!actualHash.Equals(
                                    expectedHash,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidDataException(
                                    $"ReShade package SHA-256 mismatch. Expected {expectedHash}, got {actualHash}.");
                            }
                        }
                    },
                    cancellationToken,
                    attempts: 3);

                File.Move(temp, package, true);
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

        if (!File.Exists(installer))
        {
            var extractRoot = Path.Combine(
                CacheDirectory,
                $".reshade-extract-{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(extractRoot);
                SafeZip.ExtractToDirectory(
                    package,
                    extractRoot,
                    MaxPackageBytes);

                var candidate = Directory.EnumerateFiles(
                        extractRoot,
                        "ReShade Setup.exe",
                        SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(candidate))
                {
                    throw new InvalidDataException(
                        "Manager-owned ReShade package does not contain ReShade Setup.exe.");
                }

                var installerInfo = new FileInfo(candidate);
                if (installerInfo.Length <= 0 ||
                    installerInfo.Length > MaxInstallerBytes)
                {
                    throw new InvalidDataException(
                        "Extracted ReShade installer size is invalid.");
                }

                File.Copy(candidate, installer, true);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(extractRoot))
                        Directory.Delete(extractRoot, recursive: true);
                }
                catch { }
            }
        }

        return (version, installer);
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
