using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DlssNrManager.Services;

public sealed record AiStudioPackageArchive(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("format")] string Format);

public sealed record AiStudioPackageChunk(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256);

public sealed record AiStudioPackageManifest(
    [property: JsonPropertyName("schema")] int Schema,
    [property: JsonPropertyName("package_id")] string PackageId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("release_tag")] string ReleaseTag,
    [property: JsonPropertyName("install_relative_path")] string InstallRelativePath,
    [property: JsonPropertyName("license")] string License,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("archive")] AiStudioPackageArchive Archive,
    [property: JsonPropertyName("chunks")] IReadOnlyList<AiStudioPackageChunk> Chunks);

public sealed record AiStudioPackageAvailability(
    string Tag,
    string HtmlUrl,
    AiStudioPackageManifest Manifest,
    IReadOnlyDictionary<string, AiStudioReleaseAsset> Assets);

public sealed record AiStudioReleaseAsset(
    string Url,
    string? Digest);

/// <summary>
/// Local-only installation receipt written inside the manager-owned model
/// staging directory after the actual reconstructed archive passes SHA-256.
/// It is never created for manually imported/restricted-license models.
/// </summary>
public sealed record AiStudioPackageReceipt(
    string PackageId,
    string Version,
    string ReleaseTag,
    string ArchiveSha256);

public sealed class AiStudioPackageService
{
    public const string LocalReceiptFile = ".dlssnr-manager-package.json";

    private const string ReleasesApi =
        "https://api.github.com/repos/grg914/dlss-nr-manager/releases?per_page=100";

    // Transport safety only. There is deliberately no total package-size cap.
    private const long MaxTransportAssetBytes =
        1950L * 1024 * 1024;

    // No product-level total size limit for reconstructed AI packages.
    // SafeZip still validates traversal, entry count and integer overflow.
    private const long MaxModelExpandedBytes =
        long.MaxValue;

    private const int MaxModelArchiveEntries =
        2_000_000;

    private readonly LocalAiStudioService _studio;
    private readonly HttpClient _http = new();

    public AiStudioPackageService(
        LocalAiStudioService studio)
    {
        _studio = studio;

        _http.Timeout = TimeSpan.FromHours(24);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "DlssNrManager",
                AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));
    }

    public async Task<AiStudioPackageAvailability?>
        FindLatestPackageAsync(
            string packageId,
            CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            ReleasesApi,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        using var document =
            await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

        var prefix =
            $"ai-studio-{packageId}-";

        foreach (var release in
                 document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty(
                    "draft",
                    out var draft) &&
                draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var tag =
                release.TryGetProperty(
                    "tag_name",
                    out var tagElement)
                    ? tagElement.GetString()
                    : null;

            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!release.TryGetProperty(
                    "assets",
                    out var assetsElement) ||
                assetsElement.ValueKind !=
                    JsonValueKind.Array)
            {
                continue;
            }

            var assets =
                new Dictionary<string, AiStudioReleaseAsset>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var asset in
                     assetsElement.EnumerateArray())
            {
                var name =
                    asset.TryGetProperty(
                        "name",
                        out var nameElement)
                        ? nameElement.GetString()
                        : null;

                var url =
                    asset.TryGetProperty(
                        "browser_download_url",
                        out var urlElement)
                        ? urlElement.GetString()
                        : null;

                if (string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                string? digest = null;
                if (asset.TryGetProperty(
                        "digest",
                        out var digestElement))
                {
                    digest =
                        digestElement.GetString();
                }

                assets[name] =
                    new AiStudioReleaseAsset(
                        url,
                        digest);
            }

            var manifestName =
                $"{packageId}.manifest.json";

            if (!assets.TryGetValue(
                    manifestName,
                    out var manifestAsset))
            {
                continue;
            }

            var manifest =
                await DownloadManifestAsync(
                    manifestAsset.Url,
                    cancellationToken);

            ValidateManifest(
                packageId,
                tag,
                manifest,
                assets);

            var htmlUrl =
                release.TryGetProperty(
                    "html_url",
                    out var htmlElement)
                    ? htmlElement.GetString() ?? ""
                    : "";

            return new AiStudioPackageAvailability(
                tag,
                htmlUrl,
                manifest,
                assets);
        }

        return null;
    }

    public AiStudioPackageReceipt? ReadInstalledReceipt(
        AiStudioModelDescriptor model)
    {
        if (!model.ManagerOwnedRedistributionAllowed)
            return null;

        var path = Path.Combine(
            _studio.GetModelDirectory(model),
            LocalReceiptFile);
        try
        {
            if (ManagedPathSafety.HasReparsePointOnPath(path) ||
                !File.Exists(path))
                return null;
            var receipt = JsonSerializer.Deserialize<AiStudioPackageReceipt>(
                File.ReadAllText(path));
            return receipt is not null &&
                   string.Equals(receipt.PackageId, model.Id, StringComparison.Ordinal) &&
                   receipt.ArchiveSha256.Length == 64 &&
                   receipt.ArchiveSha256.All(Uri.IsHexDigit)
                ? receipt
                : null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Compare ONLY validated manager-owned installations, never manual
    /// models or unknown/opaque/prerelease versions.
    /// </summary>
    public static MediaUpdateAvailability EvaluateModelUpdate(
        AiStudioPackageReceipt? local,
        AiStudioPackageAvailability? remote,
        bool installed,
        bool automaticRedistributionAllowed)
    {
        if (!automaticRedistributionAllowed)
            return MediaUpdateAvailability.UnknownRemoteVersion;
        if (!installed)
            return MediaUpdateAvailability.NotInstalled;
        if (local is null ||
            !TryStableVersion(local.Version, out var localVersion))
            return MediaUpdateAvailability.UnknownLocalVersion;
        if (remote is null ||
            !string.Equals(local.PackageId, remote.Manifest.PackageId, StringComparison.Ordinal) ||
            !TryStableVersion(remote.Manifest.Version, out var remoteVersion) ||
            remote.Manifest.Archive.Sha256.Length != 64 ||
            !remote.Manifest.Archive.Sha256.All(Uri.IsHexDigit))
            return MediaUpdateAvailability.UnknownRemoteVersion;
        if (remoteVersion <= localVersion ||
            string.Equals(local.ArchiveSha256, remote.Manifest.Archive.Sha256,
                StringComparison.OrdinalIgnoreCase))
            return MediaUpdateAvailability.UpToDate;
        return MediaUpdateAvailability.UpdateAvailable;
    }

    private static bool TryStableVersion(string? value, out Version parsed)
    {
        parsed = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var text = value.Trim().TrimStart('v', 'V');
        if (!text.All(ch => char.IsAsciiDigit(ch) || ch == '.') ||
            !Version.TryParse(text, out var candidate) || candidate is null)
            return false;
        parsed = candidate;
        return true;
    }

    public async Task InstallAsync(
        AiStudioModelDescriptor model,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!model.ManagerOwnedRedistributionAllowed)
        {
            throw new InvalidOperationException(
                $"{model.DisplayName} requires manual license acceptance and cannot be installed from manager-owned releases.");
        }

        _studio.EnsureWorkspace();

        progress?.Report(
            $"Recherche du package manager-owned pour {model.DisplayName}…");

        var package =
            await FindLatestPackageAsync(
                model.Id,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Aucun package manager-owned publié pour {model.DisplayName}.");

        await LargeDownloadApprovalHub.EnsureApprovedAsync(
            model.DisplayName,
            package.Manifest.Archive.Size,
            $"Installation du modèle AI Studio {model.DisplayName}",
            cancellationToken);

        var workRoot =
            Path.Combine(
                _studio.Root,
                "downloads",
                model.Id,
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(workRoot);

        var archivePath =
            Path.Combine(
                workRoot,
                package.Manifest.Archive.Name);

        using var transfer =
            DownloadProgressHub.Begin(
                model.DisplayName,
                package.Manifest.Archive.Size);

        long downloaded = 0;

        try
        {
            progress?.Report(
                $"Téléchargement de {model.DisplayName} depuis tes GitHub Releases…");

            await using (var archive =
                new FileStream(
                    archivePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    useAsync: true))
            {
                foreach (var chunk in
                         package.Manifest.Chunks
                             .OrderBy(x => x.Index))
                {
                    cancellationToken
                        .ThrowIfCancellationRequested();

                    if (!package.Assets.TryGetValue(
                            chunk.Name,
                            out var asset))
                    {
                        throw new InvalidDataException(
                            $"Release asset missing: {chunk.Name}");
                    }

                    var chunkPath =
                        Path.Combine(
                            workRoot,
                            chunk.Name);

                    downloaded = await DownloadChunkAsync(
                        asset,
                        chunk,
                        chunkPath,
                        downloaded,
                        transfer,
                        cancellationToken);

                    await using var chunkStream =
                        new FileStream(
                            chunkPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            1024 * 1024,
                            useAsync: true);

                    await chunkStream.CopyToAsync(
                        archive,
                        1024 * 1024,
                        cancellationToken);

                    TryDeleteFile(chunkPath);
                }
            }

            var archiveInfo =
                new FileInfo(archivePath);

            if (archiveInfo.Length !=
                package.Manifest.Archive.Size)
            {
                throw new InvalidDataException(
                    $"Reconstructed archive size mismatch. Expected {package.Manifest.Archive.Size}, got {archiveInfo.Length}.");
            }

            progress?.Report(
                $"Vérification SHA-256 de {model.DisplayName}…");

            var actualArchiveHash =
                await HashService.Sha256Async(
                    archivePath,
                    cancellationToken);

            if (!actualArchiveHash.Equals(
                    package.Manifest.Archive.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Reconstructed archive SHA-256 mismatch for {model.DisplayName}.");
            }

            var installPath =
                ResolveInstallPath(
                    package.Manifest.InstallRelativePath);

            ValidateModelInstallTarget(
                installPath,
                _studio.GetModelDirectory(model));

            // Previous package versions used target.backup-<GUID>.
            // Preserve unresolved legacy backups instead of overwriting them.
            var parent = Path.GetDirectoryName(installPath)!;
            if (Directory.Exists(parent) &&
                Directory.EnumerateDirectories(
                    parent,
                    Path.GetFileName(installPath) + ".backup-*",
                    SearchOption.TopDirectoryOnly).Any())
            {
                throw new InvalidOperationException(
                    "Unresolved AI Studio model backup. Review the diagnostic log before reinstalling this model.");
            }

            var staging =
                installPath +
                ".staging-" +
                Guid.NewGuid().ToString("N");

            progress?.Report(
                $"Extraction locale de {model.DisplayName}…");

            // Clean up staging even if archive extraction fails.
            try
            {
                SafeZip.Extract(
                    archivePath,
                    staging,
                    MaxModelArchiveEntries,
                    MaxModelExpandedBytes);

                // Stamp the *validated* archive inside the staged directory.
                // An interrupted transaction rolls back this receipt together
                // with the previous working model and its version.
                var receipt = new AiStudioPackageReceipt(
                    model.Id,
                    package.Manifest.Version,
                    package.Tag,
                    package.Manifest.Archive.Sha256.ToLowerInvariant());
                AtomicFile.WriteAllText(
                    Path.Combine(staging, LocalReceiptFile),
                    JsonSerializer.Serialize(receipt));

                await ManagedComponentRedownload.ReplaceAsync(
                    [installPath],
                    token =>
                    {
                        token.ThrowIfCancellationRequested();
                        Directory.Move(staging, installPath);
                        return Task.CompletedTask;
                    },
                    () => Directory.Exists(installPath) &&
                          Directory.EnumerateFileSystemEntries(installPath).Any(),
                    cancellationToken);
            }
            finally
            {
                TryDeleteDirectory(staging);
            }

            transfer.Complete();

            progress?.Report(
                $"{model.DisplayName} installé. Le modèle est maintenant disponible hors ligne.");
        }
        catch (Exception ex)
        {
            transfer.Fail(ex);
            throw;
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private async Task<long> DownloadChunkAsync(
        AiStudioReleaseAsset asset,
        AiStudioPackageChunk chunk,
        string destination,
        long alreadyDownloaded,
        DownloadProgressHandle transfer,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(
                asset.Url,
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
                $"Unexpected AI Studio release asset URL: {asset.Url}");
        }

        using var response = await _http.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var contentLength =
            response.Content.Headers.ContentLength;

        if (contentLength is > MaxTransportAssetBytes)
        {
            throw new InvalidDataException(
                "An AI Studio transport chunk exceeds the GitHub per-asset transport safety limit.");
        }

        if (contentLength is > 0 &&
            contentLength.Value != chunk.Size)
        {
            throw new InvalidDataException(
                $"Chunk size mismatch before download: {chunk.Name}.");
        }

        await using var input =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        await using var output =
            new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                useAsync: true);

        var buffer =
            new byte[1024 * 1024];

        long chunkBytes = 0;

        while (true)
        {
            var read =
                await input.ReadAsync(
                    buffer.AsMemory(
                        0,
                        buffer.Length),
                    cancellationToken);

            if (read == 0)
                break;

            chunkBytes += read;

            if (chunkBytes > MaxTransportAssetBytes ||
                chunkBytes > chunk.Size)
            {
                throw new InvalidDataException(
                    $"Chunk download exceeded expected size: {chunk.Name}.");
            }

            await output.WriteAsync(
                buffer.AsMemory(
                    0,
                    read),
                cancellationToken);

            transfer.Report(
                alreadyDownloaded +
                chunkBytes);
        }

        if (chunkBytes != chunk.Size)
        {
            throw new InvalidDataException(
                $"Downloaded chunk size mismatch: {chunk.Name}.");
        }

        await output.FlushAsync(
            cancellationToken);

        var actual =
            await HashService.Sha256Async(
                destination,
                cancellationToken);

        if (!actual.Equals(
                chunk.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Manifest SHA-256 mismatch: {chunk.Name}.");
        }

        if (!string.IsNullOrWhiteSpace(
                asset.Digest) &&
            asset.Digest.StartsWith(
                "sha256:",
                StringComparison.OrdinalIgnoreCase))
        {
            var githubSha =
                asset.Digest["sha256:".Length..];

            if (!actual.Equals(
                    githubSha,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"GitHub asset digest mismatch: {chunk.Name}.");
            }
        }

        return alreadyDownloaded +
               chunkBytes;
    }

    private async Task<AiStudioPackageManifest>
        DownloadManifestAsync(
            string url,
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
                $"Unexpected AI Studio manifest URL: {url}");
        }

        using var response =
            await _http.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > 4 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "AI Studio package manifest is unexpectedly large.");
        }

        await using var stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        return await JsonSerializer
            .DeserializeAsync<AiStudioPackageManifest>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                },
                cancellationToken)
            ?? throw new InvalidDataException(
                "AI Studio package manifest is invalid.");
    }

    private static void ValidateManifest(
        string expectedPackageId,
        string expectedTag,
        AiStudioPackageManifest manifest,
        IReadOnlyDictionary<
            string,
            AiStudioReleaseAsset> assets)
    {
        if (manifest.Schema != 1 ||
            !manifest.PackageId.Equals(
                expectedPackageId,
                StringComparison.OrdinalIgnoreCase) ||
            !manifest.ReleaseTag.Equals(
                expectedTag,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "AI Studio package manifest identity mismatch.");
        }

        if (manifest.Archive.Size <= 0 ||
            manifest.Archive.Sha256.Length != 64 ||
            manifest.Chunks.Count == 0)
        {
            throw new InvalidDataException(
                "AI Studio package manifest is incomplete.");
        }

        long total = 0;
        var expectedIndex = 1;

        foreach (var chunk in
                 manifest.Chunks
                     .OrderBy(x => x.Index))
        {
            if (chunk.Index != expectedIndex ||
                chunk.Size <= 0 ||
                chunk.Size > MaxTransportAssetBytes ||
                chunk.Sha256.Length != 64 ||
                !assets.ContainsKey(chunk.Name))
            {
                throw new InvalidDataException(
                    $"Invalid AI Studio package chunk metadata: {chunk.Name}.");
            }

            checked
            {
                total += chunk.Size;
            }

            expectedIndex++;
        }

        if (total != manifest.Archive.Size)
        {
            throw new InvalidDataException(
                "AI Studio package chunk total does not match the archive size.");
        }
    }

    private string ResolveInstallPath(
        string relative)
    {
        var normalized =
            relative.Replace(
                '/',
                Path.DirectorySeparatorChar);

        var full =
            Path.GetFullPath(
                Path.Combine(
                    _studio.Root,
                    normalized));

        var root =
            Path.GetFullPath(
                _studio.Root)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!full.StartsWith(
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "AI Studio package install path escapes the workspace.");
        }

        return full;
    }

    /// <summary>
    /// A manager-owned model package may replace only the selected model,
    /// never workspace runtime, outputs, jobs or another licensed model.
    /// </summary>
    public static string ValidateModelInstallTarget(
        string resolvedInstallPath,
        string selectedModelDirectory)
    {
        var actual = Path.GetFullPath(resolvedInstallPath);
        var expected = Path.GetFullPath(selectedModelDirectory);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "AI Studio package install path does not match the selected model directory.");
        }

        return actual;
    }

    private static void TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    private static void TryDeleteDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch { }
    }
}
