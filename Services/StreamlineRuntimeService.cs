using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record StreamlineRuntimeResult(
    string Version,
    string RuntimePath,
    string CacheDirectory,
    string SourceUrl);

public sealed class StreamlineRuntimeService
{
    private const string ManagerLatestReleaseApi =
        "https://api.github.com/repos/grg914/dlss-nr-manager/releases/latest";
    private const string ManagerVideoAsset = "video2dlssnr_release.zip";
    private const string ManagerStreamlinePrefix = "streamline-runtime-v";
    private const string ManagerStreamlineSuffix = "-win-x64.zip";
    private const long MaxStreamlineDownloadBytes = 2L * 1024 * 1024 * 1024;
    private const long MaxExtractedArchiveBytes = 8L * 1024 * 1024 * 1024;
    private const int MaxArchiveEntries = 150_000;
    private readonly HttpClient _http = new();

    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "nvidia-streamline");

    public StreamlineRuntimeService()
    {
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<StreamlineRuntimeResult> EnsureLatestDlssNrAsync(
        string gpuGeneration,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Checking manager-owned DLSS Neural Rendering runtime…");

        using var release = await GetJsonAsync(
            ManagerLatestReleaseApi,
            cancellationToken);

        var tag = release.RootElement.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString() ?? "latest"
            : "latest";

        var asset = SelectNamedAsset(release, ManagerVideoAsset)
            ?? throw new InvalidOperationException(
                $"Latest DLSS NR Manager release has no {ManagerVideoAsset} asset. " +
                "Bootstrap the validated Neural Rendering runtime first.");

        if (string.IsNullOrWhiteSpace(asset.Sha256))
        {
            throw new InvalidDataException(
                $"Manager-owned {ManagerVideoAsset} has no SHA-256 digest.");
        }

        var versionRoot = Path.Combine(
            RootDirectory,
            "manager-video-" + Sanitize(tag));
        var runtime = Path.Combine(versionRoot, "nvngx_dlssnr.dll");
        var sourceMarker = Path.Combine(versionRoot, "source-url.txt");

        if (File.Exists(runtime))
        {
            var validation = await RuntimeValidationService.ValidateAsync(
                runtime,
                gpuGeneration,
                cancellationToken);

            if (IsTrustedNvidiaRuntime(validation))
            {
                return new StreamlineRuntimeResult(
                    tag,
                    runtime,
                    versionRoot,
                    File.Exists(sourceMarker)
                        ? File.ReadAllText(sourceMarker).Trim()
                        : asset.Url);
            }

            TryDeleteDirectory(versionRoot);
        }

        Directory.CreateDirectory(versionRoot);
        var tempZip = Path.Combine(versionRoot, ManagerVideoAsset);
        var extract = Path.Combine(versionRoot, "_extract");

        progress?.Report("Downloading manager-owned NVIDIA Neural Rendering runtime…");
        await DownloadAsync(
            asset.Url,
            tempZip,
            asset.Sha256,
            cancellationToken);

        progress?.Report("Extracting validated NVIDIA Neural Rendering runtime…");
        ExtractSafe(tempZip, extract);

        var found = FindFile(extract, "nvngx_dlssnr.dll");
        if (found == null)
        {
            TryDeleteFile(tempZip);
            TryDeleteDirectory(extract);
            throw new InvalidOperationException(
                $"Manager-owned {ManagerVideoAsset} does not contain nvngx_dlssnr.dll.");
        }

        File.Copy(found, runtime, true);
        AtomicFile.WriteAllText(sourceMarker, asset.Url);

        var finalValidation = await RuntimeValidationService.ValidateAsync(
            runtime,
            gpuGeneration,
            cancellationToken);

        if (!IsTrustedNvidiaRuntime(finalValidation))
        {
            TryDeleteDirectory(versionRoot);
            throw new InvalidDataException(
                "Manager-owned nvngx_dlssnr.dll is not a trusted x64 NVIDIA-signed runtime.");
        }

        TryDeleteFile(tempZip);
        TryDeleteDirectory(extract);

        progress?.Report(
            $"Manager-owned DLSS Neural Rendering runtime ready • NVIDIA signature validated.");

        return new StreamlineRuntimeResult(
            tag,
            runtime,
            versionRoot,
            asset.Url);
    }

    public async Task<IReadOnlyList<string>> StageSelectedResourcesAsync(
        string gameDirectory,
        bool includeSuperResolution,
        bool includeFrameGeneration,
        bool includeReflex,
        bool includeNeuralRendering,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var release = await GetJsonAsync(
            ManagerLatestReleaseApi,
            cancellationToken);

        var asset = SelectManagerStreamlineAsset(release)
            ?? throw new InvalidOperationException(
                "Latest DLSS NR Manager release has no manager-owned Streamline runtime bundle.");

        if (string.IsNullOrWhiteSpace(asset.Sha256))
        {
            throw new InvalidDataException(
                $"Manager-owned Streamline asset {asset.Name} has no SHA-256 digest.");
        }

        var tag = GetStreamlineVersion(asset.Name) ?? "manager";
        var versionRoot = Path.Combine(
            RootDirectory,
            "manager-streamline-" + Sanitize(tag));
        var zip = Path.Combine(versionRoot, asset.Name);
        var work = Path.Combine(
            Path.GetTempPath(),
            "DlssNrManager",
            "streamline",
            Guid.NewGuid().ToString("N"));
        var extract = Path.Combine(work, "extract");

        Directory.CreateDirectory(versionRoot);
        Directory.CreateDirectory(work);

        try
        {
            if (!File.Exists(zip))
            {
                progress?.Report(
                    $"Downloading manager-owned Streamline {tag} resources…");
                await DownloadAsync(
                    asset.Url,
                    zip,
                    asset.Sha256,
                    cancellationToken);
            }
            else
            {
                progress?.Report(
                    $"Using cached manager-owned Streamline {tag} package…");
            }

            try
            {
                ExtractSafe(zip, extract);
            }
            catch (Exception ex) when (
                File.Exists(zip) &&
                ex is InvalidDataException or IOException)
            {
                progress?.Report(
                    $"Cached Streamline package is invalid; downloading a clean {tag} copy…");
                TryDeleteDirectory(extract);
                TryDeleteFile(zip);

                await DownloadAsync(
                    asset.Url,
                    zip,
                    asset.Sha256,
                    cancellationToken);
                ExtractSafe(zip, extract);
            }

            string? neuralRuntime = null;
            if (includeNeuralRendering)
            {
                var nr = await EnsureLatestDlssNrAsync(
                    "Unknown",
                    progress,
                    cancellationToken);
                neuralRuntime = nr.RuntimePath;
            }

            var names = new List<string>
            {
                "sl.interposer.dll",
                "sl.common.dll"
            };

            if (includeSuperResolution)
            {
                names.AddRange(
                [
                    "sl.dlss.dll",
                    "nvngx_dlss.dll",
                    "sl.dlss_d.dll",
                    "nvngx_dlssd.dll"
                ]);
            }

            if (includeFrameGeneration)
                names.AddRange(["sl.dlss_g.dll", "nvngx_dlssg.dll"]);

            if (includeReflex)
            {
                names.AddRange(
                [
                    "sl.reflex.dll",
                    "NvLowLatencyVk.dll"
                ]);
            }

            if (includeNeuralRendering)
            {
                names.AddRange(
                [
                    "sl.dlss_nr.dll",
                    "nvngx_dlssnr.dll"
                ]);
            }

            var installed = new List<string>();
            var missingRequested = new List<string>();

            try
            {
                foreach (var name in names.Distinct(
                             StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? source;
                    if (name.Equals(
                            "nvngx_dlssnr.dll",
                            StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(neuralRuntime))
                    {
                        source = neuralRuntime;
                    }
                    else
                    {
                        source = FindProductionFile(extract, name)
                                 ?? FindFile(extract, name);
                    }

                    if (source == null)
                    {
                        missingRequested.Add(name);
                        continue;
                    }

                    var destination = Path.Combine(gameDirectory, name);

                    // Never overwrite a game's existing vendor DLLs.
                    if (File.Exists(destination))
                        continue;

                    File.Copy(source, destination, false);
                    installed.Add(destination);
                }

                progress?.Report(
                    $"Staged {installed.Count} manager-owned NVIDIA Streamline/DLSS resource file(s) into the selected game.");

                if (includeNeuralRendering)
                {
                    var neuralRuntimeAvailable =
                        File.Exists(Path.Combine(
                            gameDirectory,
                            "nvngx_dlssnr.dll")) ||
                        installed.Any(path =>
                            Path.GetFileName(path).Equals(
                                "nvngx_dlssnr.dll",
                                StringComparison.OrdinalIgnoreCase));

                    if (!neuralRuntimeAvailable)
                    {
                        throw new InvalidOperationException(
                            "Manager-owned Neural Rendering runtime could not be staged.");
                    }

                    if (missingRequested.Contains(
                            "sl.dlss_nr.dll",
                            StringComparer.OrdinalIgnoreCase))
                    {
                        progress?.Report(
                            "The manager-owned Streamline bundle does not contain sl.dlss_nr.dll; " +
                            "the validated NGX Neural Rendering runtime was staged directly.");
                    }
                }

                return installed;
            }
            catch
            {
                foreach (var path in installed)
                    TryDeleteFile(path);

                throw;
            }
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    public void ClearCache() => TryDeleteDirectory(RootDirectory);

    private static bool IsTrustedNvidiaRuntime(DlssNrManager.Models.RuntimeValidation validation)
        => validation.Is64Bit
           && validation.SignatureValid
           && !string.IsNullOrWhiteSpace(validation.Publisher)
           && validation.Publisher.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);

    private static ReleaseAsset? SelectNamedAsset(
        JsonDocument release,
        string assetName)
    {
        if (!release.RootElement.TryGetProperty(
                "assets",
                out var assets))
            return null;

        return assets.EnumerateArray()
            .Select(ToReleaseAsset)
            .FirstOrDefault(asset =>
                asset.Name.Equals(
                    assetName,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(asset.Url));
    }

    private static ReleaseAsset? SelectManagerStreamlineAsset(
        JsonDocument release)
    {
        if (!release.RootElement.TryGetProperty(
                "assets",
                out var assets))
            return null;

        return assets.EnumerateArray()
            .Select(ToReleaseAsset)
            .Where(asset =>
                asset.Name.StartsWith(
                    ManagerStreamlinePrefix,
                    StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(
                    ManagerStreamlineSuffix,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(asset => asset.Name)
            .FirstOrDefault(asset =>
                !string.IsNullOrWhiteSpace(asset.Url));
    }

    private static ReleaseAsset ToReleaseAsset(
        JsonElement asset)
    {
        var digest = asset.TryGetProperty(
                "digest",
                out var digestElement)
            ? digestElement.GetString()
            : null;

        var sha256 =
            !string.IsNullOrWhiteSpace(digest) &&
            digest.StartsWith(
                "sha256:",
                StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..]
                : null;

        return new ReleaseAsset(
            asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? ""
                : "",
            asset.TryGetProperty(
                    "browser_download_url",
                    out var urlElement)
                ? urlElement.GetString() ?? ""
                : "",
            sha256);
    }

    private static string? GetStreamlineVersion(
        string assetName)
    {
        if (!assetName.StartsWith(
                ManagerStreamlinePrefix,
                StringComparison.OrdinalIgnoreCase) ||
            !assetName.EndsWith(
                ManagerStreamlineSuffix,
                StringComparison.OrdinalIgnoreCase))
            return null;

        var length =
            assetName.Length -
            ManagerStreamlinePrefix.Length -
            ManagerStreamlineSuffix.Length;

        if (length <= 0)
            return null;

        return assetName.Substring(
            ManagerStreamlinePrefix.Length,
            length);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task DownloadAsync(
        string url,
        string destination,
        string? expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(
                "github.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unexpected manager-owned runtime release URL: {url}");
        }

        var temp = destination + ".download";

        try
        {
            await NetworkRetry.ExecuteAsync(
                async (attempt, token) =>
                {
                    if (attempt > 1)
                        TryDeleteFile(temp);

                    using var response = await _http.GetAsync(
                        uri,
                        HttpCompletionOption.ResponseHeadersRead,
                        token);
                    response.EnsureSuccessStatusCode();

                    if (response.Content.Headers.ContentLength is > MaxStreamlineDownloadBytes)
                    {
                        throw new InvalidDataException(
                            "Manager-owned runtime archive exceeds the 2 GB safety limit.");
                    }

                    await using (var input =
                        await response.Content.ReadAsStreamAsync(token))
                    await using (var output = new FileStream(
                        temp,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        128 * 1024,
                        useAsync: true))
                    {
                        await CopyWithLimitAsync(
                            input,
                            output,
                            MaxStreamlineDownloadBytes,
                            token);
                    }

                    if (new FileInfo(temp).Length < 1024)
                    {
                        throw new InvalidDataException(
                            "Downloaded Streamline archive is unexpectedly small.");
                    }

                    if (!string.IsNullOrWhiteSpace(expectedSha256))
                    {
                        var actual =
                            await HashService.Sha256Async(
                                temp,
                                token);

                        if (!actual.Equals(
                                expectedSha256,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                $"Streamline archive SHA-256 mismatch. Expected {expectedSha256}, got {actual}.");
                        }
                    }
                },
                cancellationToken,
                attempts: 3);

            File.Move(temp, destination, true);
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    private static void ExtractSafe(string zipPath, string destination)
        => SafeZip.Extract(zipPath, destination);

    private static async Task CopyWithLimitAsync(
        Stream input,
        Stream output,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;

        while (true)
        {
            var read = await input.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken);

            if (read == 0)
                break;

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException(
                    $"Download exceeded the {maxBytes / (1024 * 1024)} MB safety limit.");
            }

            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }
    }

    private static string? FindProductionFile(string root, string name)
    {
        if (!Directory.Exists(root))
            return null;

        return Directory.EnumerateFiles(root, name, SearchOption.AllDirectories)
            .OrderBy(path =>
            {
                var normalized = path.Replace('\\', '/').ToLowerInvariant();
                if (normalized.Contains("/development/")) return 2;
                if (normalized.Contains("/debug/")) return 3;
                if (normalized.Contains("/production/")) return 0;
                return 1;
            })
            .FirstOrDefault();
    }

    private static string? FindFile(string root, string name)
        => Directory.Exists(root)
            ? Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault()
            : null;

    private static string Sanitize(string value)
        => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private sealed record ReleaseAsset(
        string Name,
        string Url,
        string? Sha256);
}
