using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
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
            new ProductInfoHeaderValue("DlssNrManager", "0.8.1"));
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
        await DownloadAsync(asset.Url, tempZip, cancellationToken);

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
            await DownloadAsync(asset.Url, zip, cancellationToken);
            ExtractSafe(zip, extract);

            var names = new List<string> { "sl.interposer.dll", "sl.common.dll" };
            if (includeSuperResolution)
                names.AddRange(["sl.dlss.dll", "nvngx_dlss.dll"]);
            if (includeFrameGeneration)
                names.AddRange(["sl.dlss_g.dll", "nvngx_dlssg.dll"]);
            if (includeReflex)
                names.Add("sl.reflex.dll");
            if (includeNeuralRendering)
                names.AddRange(["sl.dlss_nr.dll", "nvngx_dlssnr.dll"]);

            var installed = new List<string>();
            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var source = FindProductionFile(extract, name) ?? FindFile(extract, name);
                if (source == null)
                    continue;

                var destination = Path.Combine(gameDirectory, name);
                File.Copy(source, destination, true);
                installed.Add(destination);
            }

            progress?.Report(
                $"Staged {installed.Count} NVIDIA Streamline/DLSS resource file(s) into the selected game.");

            return installed;
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    public void ClearCache() => TryDeleteDirectory(RootDirectory);

    private static bool IsTrustedNvidiaRuntime(Models.RuntimeValidation validation)
        => validation.Is64Bit
           && validation.SignatureValid
           && !string.IsNullOrWhiteSpace(validation.Publisher)
           && validation.Publisher.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);

    private static ReleaseAsset? SelectReleaseZip(JsonDocument release)
    {
        if (!release.RootElement.TryGetProperty("assets", out var assets))
            return null;

        return assets.EnumerateArray()
            .Select(asset => new ReleaseAsset(
                asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : ""))
            .Where(x => x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Name.Contains("source", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x =>
                x.Name.Contains("streamline", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Url));
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true);
        await input.CopyToAsync(output, cancellationToken);

        if (new FileInfo(destination).Length < 1024)
            throw new InvalidDataException("Downloaded Streamline archive is unexpectedly small.");
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

    private sealed record ReleaseAsset(string Name, string Url);
}
