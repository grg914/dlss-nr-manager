using System.Text.Json;
using System.Text.Json.Serialization;

namespace DlssNrManager.Services;

public sealed record AiStudioRuntimeFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256);

public sealed record AiStudioRuntimeManifest(
    [property: JsonPropertyName("schema")] int Schema,
    [property: JsonPropertyName("package_id")] string PackageId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("files")] IReadOnlyList<AiStudioRuntimeFile> Files);

public enum AiStudioRuntimeIntegrityStatus
{
    MissingTrustedPin,
    MissingManifest,
    InvalidManifest,
    UnsafePath,
    MissingFile,
    FileHashMismatch,
    ManifestFilesVerifiedOnly
}

public sealed record AiStudioRuntimeIntegrityResult(AiStudioRuntimeIntegrityStatus Status);

// Read-only v4.5 foundation: no install, process launch or upstream network.
// The expected manifest SHA-256 MUST originate outside the candidate runtime,
// from a separately reviewed manager-owned release/component manifest.
// Matching hashes do not prove execution safety, license rights or GPU support.
public static class AiStudioRuntimeIntegrityService
{
    private const string PackageId = "ai-studio-runtime-win-x64";
    private const string ManifestName = "runtime-integrity.json";
    private const long MaxManifestBytes = 256 * 1024;
    private const int MaxEntries = 2048;

    public static async Task<AiStudioRuntimeIntegrityResult> VerifyAsync(
        string runtimeRoot,
        string? trustedManifestSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);

        AiStudioRuntimeIntegrityResult Result(AiStudioRuntimeIntegrityStatus status) => new(status);

        if (!IsHash(trustedManifestSha256))
            return Result(AiStudioRuntimeIntegrityStatus.MissingTrustedPin);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var root = Path.GetFullPath(runtimeRoot);
            if (ManagedPathSafety.HasReparsePointOnPath(root))
                return Result(AiStudioRuntimeIntegrityStatus.UnsafePath);

            var path = Path.Combine(root, ManifestName);
            if (ManagedPathSafety.HasReparsePointOnPath(path))
                return Result(AiStudioRuntimeIntegrityStatus.UnsafePath);

            if (!File.Exists(path))
                return Result(AiStudioRuntimeIntegrityStatus.MissingManifest);

            var size = new FileInfo(path).Length;
            if (size <= 0 || size > MaxManifestBytes)
                return Result(AiStudioRuntimeIntegrityStatus.InvalidManifest);

            var actualManifestHash = await HashService.Sha256Async(path, cancellationToken);
            if (!actualManifestHash.Equals(trustedManifestSha256, StringComparison.OrdinalIgnoreCase))
                return Result(AiStudioRuntimeIntegrityStatus.InvalidManifest);

            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var manifest = JsonSerializer.Deserialize<AiStudioRuntimeManifest>(json);

            if (manifest is null ||
                manifest.Schema != 1 ||
                manifest.PackageId != PackageId ||
                string.IsNullOrWhiteSpace(manifest.Version) ||
                manifest.Version.Length > 100 ||
                manifest.Files is null ||
                manifest.Files.Count is < 1 or > MaxEntries)
                return Result(AiStudioRuntimeIntegrityStatus.InvalidManifest);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry is null || !IsSafeRelativePath(entry.Path) ||
                    !seen.Add(entry.Path) || entry.Size < 0 || !IsHash(entry.Sha256))
                    return Result(AiStudioRuntimeIntegrityStatus.InvalidManifest);

                var fullPath = Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                if (ManagedPathSafety.HasReparsePointOnPath(fullPath))
                    return Result(AiStudioRuntimeIntegrityStatus.UnsafePath);

                if (!File.Exists(fullPath))
                    return Result(AiStudioRuntimeIntegrityStatus.MissingFile);

                if (new FileInfo(fullPath).Length != entry.Size)
                    return Result(AiStudioRuntimeIntegrityStatus.FileHashMismatch);

                var actualHash = await HashService.Sha256Async(fullPath, cancellationToken);
                if (!actualHash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    return Result(AiStudioRuntimeIntegrityStatus.FileHashMismatch);
            }

            // A pinned manifest verifies only its explicitly enumerated files;
            // never reinterpret this result as approval to execute Python.
            return Result(AiStudioRuntimeIntegrityStatus.ManifestFilesVerifiedOnly);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Result(AiStudioRuntimeIntegrityStatus.InvalidManifest);
        }
    }

    private static bool IsHash(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsSafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith('/') ||
            value.Contains('\\') ||
            value.Contains(':') ||
            value.Contains('\0'))
            return false;

        return value.Split('/').All(segment =>
            segment.Length is > 0 and <= 200 &&
            segment is not "." and not ".." &&
            !segment.EndsWith('.') &&
            !segment.EndsWith(' '));
    }
}
