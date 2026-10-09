using System.Security.Cryptography;
using System.Text.Json;

namespace DlssNrManager.Services;

/// <summary>
/// Import a previously reviewed and locally prepared FLUX.2 FP8 ZIP64 package.
/// No Internet, downloads, Python installation, ComfyUI execution, or source mutations.
/// Model contents are pinned independently to upstream Hugging Face SHA-256.
/// </summary>
public sealed class AiStudioFlux2OfflineImportService(LocalAiStudioService studio)
{
    private const string ModelId = "flux2-klein-4b";
    private const long MaxPartBytes = 1950L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> FileHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["diffusion_models/flux-2-klein-4b-fp8.safetensors"] =
                "97ed34fe0567e436200f2faee3939b88f2b5d99f8af2a4dc16532c4245c0ccb6",
            ["text_encoders/qwen_3_4b.safetensors"] =
                "6c671498573ac2f7a5501502ccce8d2b08ea6ca2f661c458e708f36b36edfc5a",
            ["vae/flux2-vae.safetensors"] =
                "868fe7b343cc8f3a19dbcfcafbc3d5f888802be3f89bd81b65b3621a066ce8f3",
            ["workflow-examples-not-api/image_flux2_klein_text_to_image.json"] =
                "8bb879856e54765b6c3ed219181e855981d8d10cfd19acff836603d9b56e838f",
            ["workflow-examples-not-api/image_flux2_klein_image_edit_4b_distilled.json"] =
                "2f6ea7cfd3f1d7b0628ac648c4a1090e912ea0f30b7c80a625c9831043bc7b32"
        };

    public static AiStudioPackageManifest ReadAndValidateManifest(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        if (ManagedPathSafety.HasReparsePointOnPath(manifestPath))
            throw new IOException("Offline AI package manifest is redirected.");
        var metadata = new FileInfo(manifestPath);
        if (!metadata.Exists || metadata.Length is < 32 or > 512 * 1024)
            throw new InvalidDataException("Offline AI package manifest size is invalid.");

        AiStudioPackageManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AiStudioPackageManifest>(
                File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException("Missing offline package manifest.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Malformed offline AI package manifest.", ex);
        }

        if (manifest.Schema != 1 ||
            manifest.PackageId != ModelId ||
            manifest.Version is not { Length: > 0 and <= 30 } ||
            !Version.TryParse(manifest.Version, out _) ||
            manifest.ReleaseTag != $"ai-studio-{ModelId}-{manifest.Version}" ||
            manifest.InstallRelativePath != $"models/{ModelId}" ||
            manifest.Archive is null ||
            manifest.Archive.Name != $"{ModelId}-{manifest.Version}.zip" ||
            manifest.Archive.Format != "zip" ||
            manifest.Archive.Size <= 0 ||
            !IsSha(manifest.Archive.Sha256) ||
            manifest.Chunks is null ||
            manifest.Chunks.Count is < 1 or > 1000)
            throw new InvalidDataException("Offline package identity, version or format is invalid.");

        long total = 0;
        var expectedIndex = 1;
        foreach (var chunk in manifest.Chunks.OrderBy(x => x.Index))
        {
            // These are raw ZIP transport parts, NEVER numbered 7-Zip archives.
            if (chunk.Index != expectedIndex++ ||
                chunk.Name != $"{ModelId}-{manifest.Version}.part{chunk.Index:000}" ||
                chunk.Size is <= 0 or > MaxPartBytes ||
                !IsSha(chunk.Sha256))
                throw new InvalidDataException("Untrusted offline AI package part metadata.");
            total = checked(total + chunk.Size);
        }
        if (total != manifest.Archive.Size)
            throw new InvalidDataException("Offline AI package parts do not equal archive size.");
        return manifest;
    }

    public async Task InstallAsync(
        string manifestPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var manifest = ReadAndValidateManifest(manifestPath);
        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var model = LocalAiStudioService.Models.Single(x => x.Id == ModelId);
        var target = studio.GetModelDirectory(model);
        if (ManagedPathSafety.HasReparsePointOnPath(studio.Root) ||
            ManagedPathSafety.HasReparsePointOnPath(sourceDir) ||
            ManagedPathSafety.HasReparsePointOnPath(target))
            throw new IOException("Offline package path crosses an unexpected junction or symlink.");

        await LargeDownloadApprovalHub.EnsureApprovedAsync(
            model.DisplayName, manifest.Archive.Size,
            "Import of local offline ZIP64 package; no network access.",
            cancellationToken);

        // Never update a working model if interrupted legacy transactions remain.
        var parent = Path.GetDirectoryName(target)!;
        if (Directory.Exists(parent) &&
            Directory.EnumerateDirectories(
                parent, Path.GetFileName(target) + ".backup-*", SearchOption.TopDirectoryOnly).Any())
            throw new IOException("Unresolved previous model backup; no replacement performed.");

        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        var work = Path.Combine(studio.Root, "downloads", "offline-flux2-" + Guid.NewGuid().ToString("N"));
        if (ManagedPathSafety.HasReparsePointOnPath(staging) ||
            ManagedPathSafety.HasReparsePointOnPath(work))
            throw new IOException("Unsafe offline model installation staging.");

        Directory.CreateDirectory(work);
        using var transfer = DownloadProgressHub.Begin(
            "Offline FLUX.2 FP8 import", manifest.Archive.Size);
        try
        {
            var archivePath = Path.Combine(work, manifest.Archive.Name);
            using var archiveHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long processed = 0;
            await using (var archive = new FileStream(
                archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                foreach (var chunk in manifest.Chunks.OrderBy(c => c.Index))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = Path.Combine(sourceDir, chunk.Name);
                    if (ManagedPathSafety.HasReparsePointOnPath(path))
                        throw new IOException("Offline ZIP64 part crosses an unsafe path.");
                    await using var input = new FileStream(
                        path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (input.Length != chunk.Size)
                        throw new InvalidDataException("Offline ZIP64 part length mismatch: " + chunk.Name);

                    using var partHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[1024 * 1024];
                    int count;
                    while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        partHash.AppendData(buffer, 0, count);
                        archiveHash.AppendData(buffer, 0, count);
                        await archive.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        processed = checked(processed + count);
                        transfer.Report(processed);
                    }
                    if (!string.Equals(
                        Convert.ToHexString(partHash.GetHashAndReset()),
                        chunk.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Offline ZIP64 part SHA-256 mismatch: " + chunk.Name);
                }
            }
            if (processed != manifest.Archive.Size ||
                !string.Equals(Convert.ToHexString(archiveHash.GetHashAndReset()),
                    manifest.Archive.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Reassembled offline ZIP64 archive SHA-256 mismatch.");

            progress?.Report("Archive vérifiée, extraction dans un dossier isolé…");
            SafeZip.Extract(archivePath, staging, 16, long.MaxValue);
            await VerifyExtractedFilesAsync(staging, cancellationToken);

            var receipt = new AiStudioPackageReceipt(
                ModelId, manifest.Version, manifest.ReleaseTag,
                manifest.Archive.Sha256.ToLowerInvariant());
            AtomicFile.WriteAllText(
                Path.Combine(staging, AiStudioPackageService.LocalReceiptFile),
                JsonSerializer.Serialize(receipt));

            await ManagedComponentRedownload.ReplaceAsync(
                [target],
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    Directory.Move(staging, target);
                    return Task.CompletedTask;
                },
                () => Directory.Exists(target) &&
                      Directory.EnumerateFileSystemEntries(target).Any(),
                cancellationToken);

            transfer.Complete();
            progress?.Report("Pack FLUX.2 vérifié et importé localement ; runtime ComfyUI non modifié.");
        }
        catch (Exception ex)
        {
            transfer.Fail(ex);
            throw;
        }
        finally
        {
            TryDelete(staging);
            TryDelete(work);
        }
    }

    /// <summary>
    /// On-demand full disk read of the installed model. Never run at application
    /// startup or in the background: FLUX.2 + Qwen exceed 12 GB.
    /// A matching hash proves file bytes only, not runtime/licensing approval.
    /// </summary>
    public static async Task<bool> VerifyInstalledAsync(
        string installedDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installedDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        if (ManagedPathSafety.HasReparsePointOnPath(installedDirectory) ||
            !Directory.Exists(installedDirectory))
            return false;

        var receiptPath = Path.Combine(
            installedDirectory, AiStudioPackageService.LocalReceiptFile);
        if (ManagedPathSafety.HasReparsePointOnPath(receiptPath) ||
            !File.Exists(receiptPath) ||
            new FileInfo(receiptPath).Length is <= 0 or > 8192)
            return false;

        try
        {
            var receipt = JsonSerializer.Deserialize<AiStudioPackageReceipt>(
                await File.ReadAllTextAsync(receiptPath, cancellationToken));
            if (receipt is null ||
                receipt.PackageId != ModelId ||
                receipt.ReleaseTag != $"ai-studio-{ModelId}-{receipt.Version}" ||
                !Version.TryParse(receipt.Version, out _) ||
                !IsSha(receipt.ArchiveSha256))
                return false;

            await VerifyExtractedFilesAsync(installedDirectory, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or JsonException)
        {
            return false;
        }
    }

    private static async Task VerifyExtractedFilesAsync(
        string staging, CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(
            staging, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetRelativePath(staging, path) !=
                AiStudioPackageService.LocalReceiptFile)
            .ToArray();
        if (files.Length != FileHashes.Count)
            throw new InvalidDataException("Offline FLUX.2 package has missing/extra files.");

        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ManagedPathSafety.HasReparsePointOnPath(path))
                throw new IOException("Offline model contains a redirected file.");
            var relative = Path.GetRelativePath(staging, path).Replace('\\', '/');
            if (!FileHashes.TryGetValue(relative, out var expected))
                throw new InvalidDataException("Unexpected offline model payload: " + relative);
            if (!string.Equals(
                await HashService.Sha256Async(path, cancellationToken),
                expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Wrong upstream model SHA-256: " + relative);
        }
    }

    private static bool IsSha(string? s) =>
        s is { Length: 64 } && s.All(Uri.IsHexDigit);

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                ManagedPathSafety.EnsureSafeForRemoval(directory);
                Directory.Delete(directory, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warn("Offline AI staging cleanup skipped: " + ex.Message);
        }
    }
}
