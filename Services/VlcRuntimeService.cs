using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed class VlcRuntimeService
{
    private const string ManagerLatestReleaseApi =
        "https://api.github.com/repos/grg914/dlss-nr-manager/releases/latest";

    private const string ManagerRuntimeSeedApi =
        "https://api.github.com/repos/grg914/dlss-nr-manager/releases/tags/runtime-seed-v1";

    public const string RuntimeAssetName =
        "vlc-3.0.24-win64.zip";

    public const string ProvenanceAssetName =
        "vlc-3.0.24-provenance.json";

    private const long MaxDownloadBytes =
        512L * 1024 * 1024;

    private const long MaxExpandedBytes =
        1024L * 1024 * 1024;

    private const int MaxArchiveEntries = 20000;

    private readonly HttpClient _http = new();

    public VlcRuntimeService()
    {
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "DlssNrManager",
                AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));
    }

    public string RootDirectory => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "vlc");

    public string ExecutablePath =>
        Path.Combine(RootDirectory, "vlc.exe");

    public bool IsReady =>
        File.Exists(ExecutablePath) &&
        new FileInfo(ExecutablePath).Length > 1024 * 1024;

    public void Reset()
    {
        if (Directory.Exists(RootDirectory + ".backup") ||
            File.Exists(RootDirectory + ".backup"))
            throw new IOException(
                "An unresolved VLC backup is present. Resolve it before removing the runtime.");

        ManagedPathSafety.EnsureSafeForRemoval(RootDirectory);
        TryDeleteDirectory(RootDirectory);

        if (Directory.Exists(RootDirectory))
        {
            throw new IOException(
                "Unable to remove the manager-owned VLC runtime. Close VLC and try again.");
        }
    }

    public async Task SetupAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(
            "Checking manager-owned VLC runtime…");

        // Recover interrupted installs before deciding whether to reinstall.
        var recovered = ManagedComponentRedownload.RecoverOwnedBackups([RootDirectory]);
        var legacy = ManagedComponentRedownload.RecoverLegacyVlcBackup(RootDirectory);
        if (recovered.Failed != 0 || legacy.Failed != 0)
            throw new IOException(
                "Unresolved VLC backup: previous and partial copies were retained for manual recovery.");

        if (IsReady)
        {
            progress?.Report(
                "Manager-owned VLC 3.0.24 already installed • offline-ready.");
            return;
        }

        var bundled = TryResolveBundledAsset();
        ReleaseAsset? asset = null;

        if (bundled == null)
        {
            asset = await ResolveAssetAsync(
                cancellationToken);

            if (string.IsNullOrWhiteSpace(asset.Sha256))
            {
                throw new InvalidDataException(
                    "The manager-owned VLC runtime has no GitHub SHA-256 digest.");
            }
        }

        var work = Path.Combine(
            Path.GetTempPath(),
            "dlssnr-vlc-" + Guid.NewGuid().ToString("N"));

        var zip = Path.Combine(
            work,
            RuntimeAssetName);

        var extract = Path.Combine(
            work,
            "extract");
        var staged = RootDirectory + ".staged-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(work);

            if (bundled != null)
            {
                progress?.Report(
                    "Using bundled manager-owned VLC 3.0.24 • no network required.");

                File.Copy(
                    bundled.Path,
                    zip,
                    true);

                var actual =
                    await HashService.Sha256Async(
                        zip,
                        cancellationToken);

                if (!actual.Equals(
                        bundled.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Bundled VLC runtime failed SHA-256 verification. Expected {bundled.Sha256}, got {actual}.");
                }
            }
            else
            {
                progress?.Report(
                    "Downloading manager-owned VLC 3.0.24…");

                await DownloadAsync(
                    asset!.Url,
                    zip,
                    asset.Sha256!,
                    cancellationToken);
            }

            Directory.CreateDirectory(extract);

            SafeZip.Extract(
                zip,
                extract,
                MaxArchiveEntries,
                MaxExpandedBytes);

            var vlcExe = Directory
                .EnumerateFiles(
                    extract,
                    "vlc.exe",
                    SearchOption.AllDirectories)
                .FirstOrDefault();

            if (vlcExe == null ||
                new FileInfo(vlcExe).Length < 1024 * 1024)
            {
                throw new InvalidDataException(
                    "The manager-owned VLC archive does not contain a usable vlc.exe.");
            }

            var sourceRoot =
                Path.GetDirectoryName(vlcExe)
                ?? throw new InvalidDataException(
                    "Unable to resolve the extracted VLC runtime root.");

            CopyDirectory(sourceRoot, staged);

            // The shared GUID-backup transaction is recovered at startup.
            await ManagedComponentRedownload.ReplaceAsync(
                [RootDirectory],
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    Directory.Move(staged, RootDirectory);
                    return Task.CompletedTask;
                },
                () => IsReady,
                cancellationToken);

            progress?.Report(
                "Manager-owned VLC 3.0.24 ready.");
        }
        finally
        {
            // A failed copy may leave a staged sibling; never follow a junction.
            if (Directory.Exists(staged))
            {
                try
                {
                    ManagedPathSafety.EnsureSafeForRemoval(staged);
                    TryDeleteDirectory(staged);
                }
                catch (IOException error)
                {
                    AppLogger.Warn($"Unsafe VLC staging folder preserved: {staged}. {error.Message}");
                }
            }

            TryDeleteDirectory(work);
        }
    }


    private static BundledAsset? TryResolveBundledAsset()
    {
        var runtime = Path.Combine(
            AppContext.BaseDirectory,
            RuntimeAssetName);

        var provenance = Path.Combine(
            AppContext.BaseDirectory,
            ProvenanceAssetName);

        if (!File.Exists(runtime) ||
            !File.Exists(provenance))
        {
            return null;
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    File.ReadAllText(provenance));

            if (!document.RootElement.TryGetProperty(
                    "runtime_sha256",
                    out var hashElement))
            {
                return null;
            }

            var hash =
                hashElement.GetString();

            if (string.IsNullOrWhiteSpace(hash) ||
                hash.Length != 64 ||
                hash.Any(ch => !Uri.IsHexDigit(ch)))
            {
                return null;
            }

            return new BundledAsset(
                runtime,
                hash.ToLowerInvariant());
        }
        catch
        {
            return null;
        }
    }

    private async Task<ReleaseAsset> ResolveAssetAsync(
        CancellationToken cancellationToken)
    {
        foreach (var api in new[]
                 {
                     ManagerLatestReleaseApi,
                     ManagerRuntimeSeedApi
                 })
        {
            var asset = await TryResolveAssetAsync(
                api,
                cancellationToken);

            if (asset != null)
                return asset;
        }

        throw new InvalidOperationException(
            $"Manager-owned VLC asset {RuntimeAssetName} is missing from both the latest DLSS NR Manager release and runtime-seed-v1.");
    }

    private async Task<ReleaseAsset?> TryResolveAssetAsync(
        string apiUrl,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
            apiUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        using var document =
            await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? ""
                : "";

            if (!name.Equals(RuntimeAssetName, StringComparison.OrdinalIgnoreCase))
                continue;

            var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString() ?? ""
                : "";

            string? sha256 = null;
            if (asset.TryGetProperty("digest", out var digestElement))
            {
                var digest = digestElement.GetString();
                if (!string.IsNullOrWhiteSpace(digest) &&
                    digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    sha256 = digest["sha256:".Length..];
                }
            }

            if (string.IsNullOrWhiteSpace(url))
                return null;

            return new ReleaseAsset(url, sha256);
        }

        return null;
    }

    private async Task DownloadAsync(
        string url,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri) ||
            !uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(
                "github.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unexpected VLC runtime URL: {url}");
        }

        var temp = destination + ".download";

        try
        {
            await NetworkRetry.ExecuteAsync(
                async (attempt, token) =>
                {
                    if (attempt > 1)
                    {
                        TryDeleteFile(temp);
                    }

                    using var response = await _http.GetAsync(
                        uri,
                        HttpCompletionOption.ResponseHeadersRead,
                        token);

                    response.EnsureSuccessStatusCode();

                    if (response.Content.Headers.ContentLength
                        is > MaxDownloadBytes)
                    {
                        throw new InvalidDataException(
                            "VLC runtime archive exceeds the 512 MB safety limit.");
                    }

                    await using var input =
                        await response.Content.ReadAsStreamAsync(
                            token);

                    await using var output =
                        new FileStream(
                            temp,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            128 * 1024,
                            useAsync: true);

                    var buffer =
                        new byte[128 * 1024];

                    long total = 0;

                    while (true)
                    {
                        var read = await input.ReadAsync(
                            buffer.AsMemory(),
                            token);

                        if (read == 0)
                            break;

                        total += read;

                        if (total > MaxDownloadBytes)
                        {
                            throw new InvalidDataException(
                                "VLC runtime download exceeded the 512 MB safety limit.");
                        }

                        await output.WriteAsync(
                            buffer.AsMemory(0, read),
                            token);
                    }

                    if (new FileInfo(temp).Length < 10 * 1024 * 1024)
                    {
                        throw new InvalidDataException(
                            "Downloaded VLC runtime archive is unexpectedly small.");
                    }

                    var actual =
                        await HashService.Sha256Async(
                            temp,
                            token);

                    if (!actual.Equals(
                            expectedSha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"Downloaded VLC runtime failed SHA-256 verification. Expected {expectedSha256}, got {actual}.");
                    }
                },
                cancellationToken,
                attempts: 3);

            File.Move(
                temp,
                destination,
                true);
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    private static void CopyDirectory(
        string source,
        string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in
                 Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(
                Path.Combine(
                    destination,
                    Path.GetRelativePath(
                        source,
                        directory)));
        }

        foreach (var file in
                 Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            var target = Path.Combine(
                destination,
                Path.GetRelativePath(
                    source,
                    file));

            Directory.CreateDirectory(
                Path.GetDirectoryName(target)!);

            File.Copy(
                file,
                target,
                true);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private sealed record BundledAsset(
        string Path,
        string Sha256);

    private sealed record ReleaseAsset(
        string Url,
        string? Sha256);
}
