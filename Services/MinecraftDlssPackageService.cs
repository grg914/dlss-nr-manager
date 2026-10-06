using System.IO.Compression;
using System.Security.Cryptography;

namespace DlssNrManager.Services;

public sealed record MinecraftDlssFeatureSelection(
    bool SuperResolution,
    bool FrameGeneration,
    bool Reflex,
    bool NeuralRendering);

public sealed record MinecraftDlssPackageInspection(
    IReadOnlyList<string> PresentFiles,
    IReadOnlyList<string> MissingRequiredFiles,
    IReadOnlyDictionary<string, string> Sha256);

public sealed class MinecraftDlssPackageService
{
    private static readonly string[] Core =
    {
        "sl.interposer.dll",
        "sl.common.dll"
    };

    private static readonly string[] SuperResolution =
    {
        "sl.dlss.dll",
        "nvngx_dlss.dll"
    };

    private static readonly string[] FrameGeneration =
    {
        "sl.dlss_g.dll",
        "nvngx_dlssg.dll"
    };

    private static readonly string[] Reflex =
    {
        "sl.reflex.dll"
    };

    private static readonly string[] NeuralRendering =
    {
        "sl.dlss_nr.dll",
        "nvngx_dlssnr.dll"
    };

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

    public MinecraftDlssPackageInspection Inspect(
        string zipPath,
        MinecraftDlssFeatureSelection selection)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("DLSS package ZIP was not found.", zipPath);

        using var zip = ZipFile.OpenRead(zipPath);

        var entries = zip.Entries
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.First(),
                StringComparer.OrdinalIgnoreCase);

        var required = RequiredFiles(selection).ToArray();
        var missing = required.Where(x => !entries.ContainsKey(x)).ToArray();
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in required)
        {
            if (!entries.TryGetValue(name, out var entry))
                continue;

            using var stream = entry.Open();
            hashes[name] = Convert.ToHexString(SHA256.HashData(stream));
        }

        return new MinecraftDlssPackageInspection(
            entries.Keys.OrderBy(x => x).ToArray(),
            missing,
            hashes);
    }

    public IReadOnlyList<string> StageSelectedRuntime(
        string zipPath,
        string instanceRoot,
        MinecraftDlssFeatureSelection selection,
        bool requireValidatedHashes = true)
    {
        var inspection = Inspect(zipPath, selection);

        if (inspection.MissingRequiredFiles.Count != 0)
        {
            throw new InvalidDataException(
                "The selected package is missing: "
                + string.Join(", ", inspection.MissingRequiredFiles));
        }

        if (requireValidatedHashes)
        {
            foreach (var name in RequiredFiles(selection))
            {
                if (!ValidatedHashes.TryGetValue(name, out var expected))
                {
                    throw new InvalidDataException(
                        $"No validated fingerprint is registered for {name}.");
                }

                if (!inspection.Sha256.TryGetValue(name, out var actual) ||
                    !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"{name} does not match the validated package fingerprint. Expected {expected}, got {actual ?? "missing"}.");
                }
            }
        }

        var root = Path.GetFullPath(instanceRoot);
        var destinationRoot = Path.Combine(root, ".dlss-nr-manager-runtime");
        Directory.CreateDirectory(destinationRoot);

        using var zip = ZipFile.OpenRead(zipPath);

        var entries = zip.Entries
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.First(),
                StringComparer.OrdinalIgnoreCase);

        var installed = new List<string>();

        foreach (var name in RequiredFiles(selection))
        {
            var entry = entries[name];
            using var input = entry.Open();
            using var memory = new MemoryStream();
            input.CopyTo(memory);
            var bytes = memory.ToArray();

            var destination = Path.Combine(destinationRoot, name);
            AtomicFile.WriteAllBytes(destination, bytes);
            installed.Add(Path.GetRelativePath(root, destination));
        }

        return installed;
    }

    public string StageNeuralRenderingRuntime(
        string dllPath,
        string instanceRoot,
        bool requireValidatedHash = true)
    {
        if (!File.Exists(dllPath))
            throw new FileNotFoundException("nvngx_dlssnr.dll was not found.", dllPath);

        var bytes = File.ReadAllBytes(dllPath);
        var actual = Convert.ToHexString(SHA256.HashData(bytes));

        if (requireValidatedHash)
        {
            var expected = ValidatedHashes["nvngx_dlssnr.dll"];
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"nvngx_dlssnr.dll hash mismatch. Expected {expected}, got {actual}.");
            }
        }

        var destinationRoot = Path.Combine(
            Path.GetFullPath(instanceRoot),
            ".dlss-nr-manager-runtime");

        Directory.CreateDirectory(destinationRoot);

        var destination = Path.Combine(destinationRoot, "nvngx_dlssnr.dll");
        AtomicFile.WriteAllBytes(destination, bytes);
        return destination;
    }

    public void ClearStagedRuntime(string instanceRoot)
    {
        var destinationRoot = Path.Combine(
            Path.GetFullPath(instanceRoot),
            ".dlss-nr-manager-runtime");

        if (Directory.Exists(destinationRoot))
            Directory.Delete(destinationRoot, true);
    }

    public static IEnumerable<string> RequiredFiles(
        MinecraftDlssFeatureSelection selection)
    {
        foreach (var file in Core)
            yield return file;

        if (selection.SuperResolution)
            foreach (var file in SuperResolution)
                yield return file;

        if (selection.FrameGeneration)
            foreach (var file in FrameGeneration)
                yield return file;

        if (selection.Reflex)
            foreach (var file in Reflex)
                yield return file;

        if (selection.NeuralRendering)
            foreach (var file in NeuralRendering)
                yield return file;
    }
}
