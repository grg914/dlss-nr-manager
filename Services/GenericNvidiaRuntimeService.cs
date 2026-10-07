using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record GenericNvidiaFeatureSelection(
    bool SuperResolution,
    bool FrameGeneration,
    bool Reflex,
    bool NeuralRendering);

public sealed record GenericNvidiaPackageInspection(
    IReadOnlyList<string> PresentFiles,
    IReadOnlyList<string> MissingRequiredFiles,
    IReadOnlyDictionary<string, string> Sha256);

public sealed record GenericNvidiaCleanupResult(
    int RemovedFiles,
    int PreservedModifiedFiles);

public sealed class GenericNvidiaRuntimeService
{
    private const string ManifestFile = ".dlss-nr-manager-nvidia-runtime.json";

    private static readonly IReadOnlyDictionary<string, string> ValidatedHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sl.interposer.dll"] = "27B2190057994C0B287C2C5716953BF1586F6499AC12FBBB2092B9AAF8396570",
            ["sl.common.dll"] = "A4B2B5ACBE49FBC6D44DD432CAC19CD53218F698B2539DC7ED0FB268C72CFC8D",
            ["sl.dlss.dll"] = "1EB5FB3D6F01D340FE086D981CC2DE4F18AA6D05EE276E5CF28ECD54818DCC8B",
            ["nvngx_dlss.dll"] = "C85F971CE023C9F3492FC7455F0B01A24BA18EA39636407A846902C4360B0B7E",
            ["sl.dlss_g.dll"] = "B8B5EFFD7DEBDB750ABD216DE43385FB653261712BC315D85EBA68811FB3EE02",
            ["nvngx_dlssg.dll"] = "5D5CBF14D2727D47F93FD10BF77BD91708AE122482A6F86FD564971641EBD47B",
            ["sl.reflex.dll"] = "ECF12973CDCEC2FFCED2EA77B1C7E45F4D387E7C864DDB5531B66A6F947EFFB3",
            ["sl.dlss_nr.dll"] = "9F6672E5E0170DC118A3188D21BDA187E1FC1AA3502895B21AB846D23165C11D",
            ["nvngx_dlssnr.dll"] = "E16BCF15E16E13F527491CDF7845B2FE6521A738D8F7C9C721866A8496E1FC8E"
        };

    private readonly StreamlineRuntimeService _streamline = new();

    public Task<StreamlineRuntimeResult> EnsureLatestNeuralRuntimeAsync(
        string gpuGeneration,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
        => _streamline.EnsureLatestDlssNrAsync(
            gpuGeneration,
            progress,
            cancellationToken);

    public GenericNvidiaPackageInspection InspectLocalPackage(
        string zipPath,
        GenericNvidiaFeatureSelection selection)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("DLSS / Streamline package ZIP was not found.", zipPath);

        using var zip = ZipFile.OpenRead(zipPath);

        var entries = zip.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var required = RequiredFiles(selection).ToArray();
        var missing = required.Where(name => !entries.ContainsKey(name)).ToArray();
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in required)
        {
            if (!entries.TryGetValue(name, out var entry))
                continue;

            using var stream = entry.Open();
            hashes[name] = Convert.ToHexString(SHA256.HashData(stream));
        }

        return new GenericNvidiaPackageInspection(
            entries.Keys.OrderBy(name => name).ToArray(),
            missing,
            hashes);
    }

    public async Task<IReadOnlyList<string>> StageManagerOwnedAsync(
        string gameDirectory,
        GenericNvidiaFeatureSelection selection,
        IProgress<string>? progress = null,
        bool trackForManualCleanup = true,
        CancellationToken cancellationToken = default)
    {
        var root = EnsureGenericGameTarget(gameDirectory);

        var installed = await _streamline.StageSelectedResourcesAsync(
            root,
            selection.SuperResolution,
            selection.FrameGeneration,
            selection.Reflex,
            selection.NeuralRendering,
            progress,
            cancellationToken);

        if (trackForManualCleanup)
            RecordManagedFiles(root, installed);

        return installed;
    }

    public IReadOnlyList<string> StageLocalPackage(
        string zipPath,
        string gameDirectory,
        GenericNvidiaFeatureSelection selection,
        bool requireValidatedHashes = true)
    {
        var root = EnsureGenericGameTarget(gameDirectory);
        var inspection = InspectLocalPackage(zipPath, selection);

        if (inspection.MissingRequiredFiles.Count != 0)
            throw new InvalidDataException(
                "The selected package is missing: " +
                string.Join(", ", inspection.MissingRequiredFiles));

        if (requireValidatedHashes)
        {
            foreach (var name in RequiredFiles(selection))
            {
                if (!ValidatedHashes.TryGetValue(name, out var expected))
                    throw new InvalidDataException(
                        $"No validated fingerprint is registered for {name}.");

                if (!inspection.Sha256.TryGetValue(name, out var actual) ||
                    !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"{name} does not match the validated runtime fingerprint. Expected {expected}, got {actual ?? "missing"}.");
            }
        }

        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var installed = new List<string>();

        try
        {
            foreach (var name in RequiredFiles(selection))
            {
                var destination = Path.Combine(root, name);
                if (File.Exists(destination))
                    continue;

                using var input = entries[name].Open();
                using var memory = new MemoryStream();
                input.CopyTo(memory);
                AtomicFile.WriteAllBytes(destination, memory.ToArray());
                installed.Add(destination);
            }

            RecordManagedFiles(root, installed);
            return installed;
        }
        catch
        {
            foreach (var path in installed)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            throw;
        }
    }

    public GenericNvidiaCleanupResult ClearManagedRuntime(string gameDirectory)
    {
        var root = Path.GetFullPath(gameDirectory);
        var manifestPath = Path.Combine(root, ManifestFile);

        if (!File.Exists(manifestPath))
            return new GenericNvidiaCleanupResult(0, 0);

        ManagedRuntimeManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ManagedRuntimeManifest>(
                           File.ReadAllText(manifestPath))
                       ?? throw new InvalidDataException(
                           "Managed NVIDIA runtime manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Managed NVIDIA runtime manifest is invalid; refusing destructive cleanup.",
                ex);
        }

        var preserved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var removed = 0;

        foreach (var pair in manifest.Files)
        {
            if (!IsSafeRuntimeFileName(pair.Key))
                continue;

            var path = Path.Combine(root, pair.Key);
            if (!File.Exists(path))
                continue;

            var current = HashService.Sha256(path);
            if (!current.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                preserved[pair.Key] = pair.Value;
                continue;
            }

            File.Delete(path);
            removed++;
        }

        if (preserved.Count == 0)
        {
            File.Delete(manifestPath);
        }
        else
        {
            AtomicFile.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(
                    new ManagedRuntimeManifest(preserved),
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        return new GenericNvidiaCleanupResult(removed, preserved.Count);
    }

    public static IEnumerable<string> RequiredFiles(
        GenericNvidiaFeatureSelection selection)
    {
        yield return "sl.interposer.dll";
        yield return "sl.common.dll";

        if (selection.SuperResolution)
        {
            yield return "sl.dlss.dll";
            yield return "nvngx_dlss.dll";
        }

        if (selection.FrameGeneration)
        {
            yield return "sl.dlss_g.dll";
            yield return "nvngx_dlssg.dll";
        }

        if (selection.Reflex)
            yield return "sl.reflex.dll";

        if (selection.NeuralRendering)
        {
            yield return "sl.dlss_nr.dll";
            yield return "nvngx_dlssnr.dll";
        }
    }

    private static string EnsureGenericGameTarget(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            throw new ArgumentException("Game directory is required.", nameof(gameDirectory));

        var root = Path.GetFullPath(gameDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        if (LooksLikeMinecraftJavaInstance(root))
            throw new InvalidOperationException(
                "Generic NVIDIA runtime staging is blocked for Minecraft Java instances. Use the Minecraft RTX page so the protected Caustica Vulkan/NGX policy remains authoritative.");

        return root;
    }

    private static bool LooksLikeMinecraftJavaInstance(string root)
    {
        var hasResourcePacks = Directory.Exists(Path.Combine(root, "resourcepacks"));
        var hasMods = Directory.Exists(Path.Combine(root, "mods"));
        var hasOptions = File.Exists(Path.Combine(root, "options.txt"));
        var hasVersions = Directory.Exists(Path.Combine(root, "versions"));
        var hasLauncher =
            File.Exists(Path.Combine(root, "launcher_profiles.json")) ||
            File.Exists(Path.Combine(root, "launcher_accounts.json"));

        return (hasResourcePacks && (hasMods || hasOptions)) ||
               (hasVersions && hasLauncher);
    }

    private static void RecordManagedFiles(
        string root,
        IEnumerable<string> absolutePaths)
    {
        var manifestPath = Path.Combine(root, ManifestFile);
        var files = ReadManagedFiles(manifestPath);
        var normalizedRoot =
            root.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        foreach (var path in absolutePaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            var full = Path.GetFullPath(path);
            if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            var name = Path.GetFileName(full);
            if (!IsSafeRuntimeFileName(name))
                continue;

            files[name] = HashService.Sha256(full);
        }

        if (files.Count == 0)
            return;

        AtomicFile.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(
                new ManagedRuntimeManifest(files),
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Dictionary<string, string> ReadManagedFiles(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var manifest = JsonSerializer.Deserialize<ManagedRuntimeManifest>(
                File.ReadAllText(manifestPath));

            return new Dictionary<string, string>(
                manifest?.Files ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            throw new InvalidDataException(
                "Managed NVIDIA runtime manifest is invalid; refusing to replace its ownership state.");
        }
    }

    private static bool IsSafeRuntimeFileName(string name)
        => !string.IsNullOrWhiteSpace(name) &&
           Path.GetFileName(name).Equals(name, StringComparison.OrdinalIgnoreCase) &&
           name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private sealed record ManagedRuntimeManifest(
        Dictionary<string, string> Files);
}
