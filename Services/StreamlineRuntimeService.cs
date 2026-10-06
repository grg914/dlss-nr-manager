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
    private const string Repository = "NVIDIA-RTX/Streamline";
    private readonly HttpClient _http = new();

    public string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "nvidia-streamline");

    public StreamlineRuntimeService()
    {
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", "1.2"));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<StreamlineRuntimeResult> EnsureLatestDlssNrAsync(
        string gpuGeneration,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Checking NVIDIA Streamline release…");

        using var release = await GetJsonAsync(
            $"https://api.github.com/repos/{Repository}/releases/latest",
            cancellationToken);

        var tag = release.RootElement.GetProperty("tag_name").GetString()
            ?? throw new InvalidDataException("Streamline release has no tag.");

        var asset = SelectReleaseZip(release)
            ?? throw new InvalidDataException(
                "No Streamline SDK ZIP was found in the latest NVIDIA release.");

        var versionRoot = Path.Combine(RootDirectory, Sanitize(tag));
        var runtime = Path.Combine(versionRoot, "nvngx_dlssnr.dll");
        var sourceMarker = Path.Combine(versionRoot, "source-url.txt");

        if (File.Exists(runtime))
        {
            var validation = await RuntimeValidationService.ValidateAsync(runtime, gpuGeneration);
            if (IsTrustedNvidiaRuntime(validation))
            {
                return new StreamlineRuntimeResult(
                    tag,
                    runtime,
                    versionRoot,
                    File.Exists(sourceMarker) ? File.ReadAllText(sourceMarker).Trim() : asset.Url);
            }

            TryDeleteDirectory(versionRoot);
        }

        Directory.CreateDirectory(versionRoot);
        var tempZip = Path.Combine(versionRoot, "streamline.zip");
        var extract = Path.Combine(versionRoot, "_extract");

        progress?.Report($"Downloading NVIDIA Streamline {tag}…");
        await DownloadAsync(
            asset.Url,
            tempZip,
            asset.Sha256,
            cancellationToken);

        progress?.Report("Extracting NVIDIA Streamline production runtime…");
        ExtractSafe(tempZip, extract);

        var found = FindProductionFile(extract, "nvngx_dlssnr.dll")
            ?? FindFile(extract, "nvngx_dlssnr.dll")
            ?? throw new InvalidDataException(
                "The Streamline release does not contain nvngx_dlssnr.dll.");

        File.Copy(found, runtime, true);
        File.WriteAllText(sourceMarker, asset.Url);

        var finalValidation = await RuntimeValidationService.ValidateAsync(runtime, gpuGeneration);
        if (!IsTrustedNvidiaRuntime(finalValidation))
        {
            TryDeleteDirectory(versionRoot);
            throw new InvalidDataException(
                "Downloaded nvngx_dlssnr.dll is not a trusted x64 NVIDIA-signed runtime.");
        }

        TryDeleteFile(tempZip);
        TryDeleteDirectory(extract);

        progress?.Report(
            $"NVIDIA Streamline {tag} ready • DLSS Neural Rendering runtime validated.");

        return new StreamlineRuntimeResult(tag, runtime, versionRoot, asset.Url);
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
            $"https://api.github.com/repos/{Repository}/releases/latest",
            cancellationToken);

        var tag = release.RootElement.GetProperty("tag_name").GetString() ?? "latest";
        var asset = SelectReleaseZip(release)
            ?? throw new InvalidDataException("No Streamline SDK ZIP was found.");

        var work = Path.Combine(Path.GetTempPath(), "DlssNrManager", "streamline", Guid.NewGuid().ToString("N"));
        var zip = Path.Combine(work, "streamline.zip");
        var extract = Path.Combine(work, "extract");
        Directory.CreateDirectory(work);

        try
        {
            progress?.Report($"Downloading NVIDIA Streamline {tag} resources…");
            await DownloadAsync(
                asset.Url,
                zip,
                asset.Sha256,
                cancellationToken);
            ExtractSafe(zip, extract);

            var names = new List<string> { "sl.interposer.dll", "sl.common.dll" };
            if (includeSuperResolution)
            {
                names.AddRange(
                [
                    "sl.dlss.dll",
                    "nvngx_dlss.dll",
                    // DLSS Ray Reconstruction / DLSS-D. These are optional
                    // unless the renderer integrates RR, but are safe to stage
                    // when present in the official Streamline package.
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
                    // Required by the Streamline Reflex Vulkan path when the
                    // package ships it.
                    "NvLowLatencyVk.dll"
                ]);
            }

            if (includeNeuralRendering)
                names.AddRange(["sl.dlss_nr.dll", "nvngx_dlssnr.dll"]);

            var installed = new List<string>();

            try
            {
                foreach (var name in names.Distinct(
                             StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var source = FindProductionFile(extract, name)
                                 ?? FindFile(extract, name);
                    if (source == null)
                        continue;

                    var destination = Path.Combine(gameDirectory, name);

                    // Never overwrite a game's existing vendor DLLs.
                    if (File.Exists(destination))
                        continue;

                    File.Copy(source, destination, false);
                    installed.Add(destination);
                }

                progress?.Report(
                    $"Staged {installed.Count} NVIDIA Streamline/DLSS resource file(s) into the selected game.");

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

    private static ReleaseAsset? SelectReleaseZip(JsonDocument release)
    {
        if (!release.RootElement.TryGetProperty("assets", out var assets))
            return null;

        return assets.EnumerateArray()
            .Select(asset =>
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
                    asset.TryGetProperty("name", out var n)
                        ? n.GetString() ?? ""
                        : "",
                    asset.TryGetProperty("browser_download_url", out var u)
                        ? u.GetString() ?? ""
                        : "",
                    sha256);
            })
            .Where(x =>
                x.Name.EndsWith(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            .Where(x =>
                !x.Name.Contains(
                    "source",
                    StringComparison.OrdinalIgnoreCase))
            .Where(x =>
                !ContainsArchitectureToken(
                    x.Name,
                    "aarch64",
                    "arm64ec",
                    "arm64"))
            .OrderByDescending(x =>
                IsPreferredWindowsX64Package(x.Name))
            .ThenBy(x => x.Name.Length)
            .FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.Url));
    }

    private static int IsPreferredWindowsX64Package(
        string name)
    {
        var normalized = name.ToLowerInvariant();

        if (normalized.Contains("win64") ||
            normalized.Contains("windows-x64") ||
            normalized.Contains("x86_64") ||
            normalized.Contains("x64"))
            return 3;

        // NVIDIA's current x64 package is the architecture-less
        // streamline-sdk-vX.Y.Z.zip asset.
        if (normalized.StartsWith("streamline-sdk-") &&
            normalized.EndsWith(".zip") &&
            !normalized.Contains("arm"))
            return 2;

        return 1;
    }

    private static bool ContainsArchitectureToken(
        string value,
        params string[] tokens)
        => tokens.Any(token =>
            value.Contains(
                token,
                StringComparison.OrdinalIgnoreCase));

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
        var temp = destination + ".download";

        try
        {
            using var response = await _http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var input =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);
            await using var output = new FileStream(
                temp,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                true);

            await input.CopyToAsync(
                output,
                cancellationToken);

            if (new FileInfo(temp).Length < 1024)
            {
                throw new InvalidDataException(
                    "Downloaded Streamline archive is unexpectedly small.");
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                await using var hashStream = File.OpenRead(temp);
                var actual = Convert.ToHexString(
                    await SHA256.HashDataAsync(
                        hashStream,
                        cancellationToken));

                if (!actual.Equals(
                        expectedSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Streamline archive SHA-256 mismatch. Expected {expectedSha256}, got {actual}.");
                }
            }

            File.Move(temp, destination, true);
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    private static void ExtractSafe(string zipPath, string destination)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsafe archive entry: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
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
