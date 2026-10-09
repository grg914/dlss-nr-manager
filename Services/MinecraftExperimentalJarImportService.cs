using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DlssNrManager.Services;

/// <summary>
/// Explicit offline import of one pinned Caustica CI experiment. Never downloads a JAR,
/// modifies release assets, or permits unreviewed arbitrary Minecraft mod execution.
/// </summary>
public sealed class MinecraftExperimentalJarImportService
{
    public const string BuildSource = "Caustica RTX ReSTIR CI PR #30";
    public const string BuildCommit = "f029e6aebfcab092e00772fcf551ca4b5f037510";
    public const string DlssSdkBuildVersion = "310.7.0";
    public const string ExperimentalJarName = "Caustica-RTX-ReSTIR-PR30-CI.jar";
    public const string ApprovedJarSha256 =
        "b672d8d1ac9caca2f2a182ac4e36e9841f080560ba9a7afe4b222f18a71d1a8b";

    private const string ReceiptName = "receipt.json";
    private const string BackupFolder = "caustica-experimental";
    private readonly string _approvedSha256;
    private readonly MinecraftRestirExperimentService _restir = new();

    public MinecraftExperimentalJarImportService() : this(ApprovedJarSha256) { }

    internal MinecraftExperimentalJarImportService(string approvedSha256)
        => _approvedSha256 = approvedSha256;

    // A backup without a receipt means the import was interrupted. Treat it as
    // active so managed installs cannot overwrite recoverable user data.
    public bool IsImported(string minecraftRoot)
        => Directory.Exists(BackupDirectory(minecraftRoot)) ||
           File.Exists(ReceiptPath(minecraftRoot));

    public bool CanRestore(string minecraftRoot)
        => Directory.Exists(BackupDirectory(minecraftRoot)) &&
           File.Exists(ReceiptPath(minecraftRoot));

    public bool HasInterruptedImport(string minecraftRoot)
        => IsImported(minecraftRoot) && !CanRestore(minecraftRoot);

    public void Import(string minecraftRoot, string sourceJar)
    {
        var root = Path.GetFullPath(minecraftRoot);
        var mods = Path.Combine(root, "mods");
        var backup = BackupDirectory(root);
        var receiptPath = ReceiptPath(root);

        RequirePlainDirectory(root);
        RequirePlainDirectory(mods);
        RequirePlainDirectoryIfPresent(Path.GetDirectoryName(backup)!);
        if (Directory.Exists(backup) || File.Exists(receiptPath))
            throw new IOException("An experimental Caustica backup already exists. Restore it before importing again.");

        var candidates = Directory.EnumerateFiles(mods, "*.jar", SearchOption.TopDirectoryOnly)
            .Where(file => Path.GetFileName(file).Contains("caustica", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length != 1)
            throw new InvalidOperationException("An existing, single Caustica JAR is required for a reversible import.");

        var original = candidates[0];
        RequirePlainFile(original);
        var originalName = Path.GetFileName(original);
        if (originalName.Equals(ExperimentalJarName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The experimental Caustica JAR is already present.");

        RequirePlainFile(sourceJar);
        ValidatePinnedJar(sourceJar);

        var experimental = Path.Combine(mods, ExperimentalJarName);
        if (File.Exists(experimental))
            throw new IOException("The experimental destination already exists.");

        var staging = Path.Combine(mods, ".caustica-experiment-" + Guid.NewGuid().ToString("N") + ".tmp");
        var backedUp = Path.Combine(backup, "original.jar");
        var movedOriginal = false;
        var imported = false;
        var originalHash = Sha256(original);

        try
        {
            // Stage and verify all bytes before removing any live mod.
            File.Copy(sourceJar, staging, overwrite: false);
            ValidatePinnedJar(staging);
            Directory.CreateDirectory(backup);
            RequirePlainDirectory(backup);
            File.Move(original, backedUp);
            movedOriginal = true;
            File.Move(staging, experimental);
            imported = true;

            if (!_restir.Inspect(root).Available)
                throw new InvalidDataException("The imported Caustica JAR failed the experimental ReSTIR capability check.");

            var receipt = new Receipt(originalName, originalHash, ExperimentalJarName,
                _approvedSha256, BuildCommit, DlssSdkBuildVersion);
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt));
        }
        catch
        {
            // Preserve the backup if recovery itself fails. Never delete the user's
            // original JAR merely to hide a failed import.
            if (imported && File.Exists(experimental))
                File.Delete(experimental);
            if (movedOriginal && File.Exists(backedUp) && !File.Exists(original))
                File.Move(backedUp, original);
            if (!File.Exists(backedUp) && Directory.Exists(backup))
                Directory.Delete(backup, recursive: false);
            throw;
        }
        finally
        {
            if (File.Exists(staging))
                File.Delete(staging);
        }
    }

    public void Restore(string minecraftRoot)
    {
        var root = Path.GetFullPath(minecraftRoot);
        var mods = Path.Combine(root, "mods");
        var backup = BackupDirectory(root);
        var receiptPath = ReceiptPath(root);

        RequirePlainDirectory(root);
        RequirePlainDirectory(mods);
        RequirePlainDirectoryIfPresent(Path.GetDirectoryName(backup)!);
        RequirePlainDirectory(backup);
        RequirePlainFile(receiptPath);

        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(receiptPath))
            ?? throw new InvalidDataException("Invalid experimental import receipt.");
        if (Path.GetFileName(receipt.OriginalName) != receipt.OriginalName
            || !receipt.ExperimentalName.Equals(ExperimentalJarName, StringComparison.Ordinal)
            || !receipt.ExperimentalSha256.Equals(_approvedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Experimental Caustica receipt is not valid.");

        var original = Path.Combine(mods, receipt.OriginalName);
        var experimental = Path.Combine(mods, ExperimentalJarName);
        var backedUp = Path.Combine(backup, "original.jar");
        RequirePlainFile(backedUp);
        RequirePlainFile(experimental);

        // A manager-owned backup is never a general cleanup directory.
        // Stop before any live-file changes if unrelated files were added.
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "original.jar", ReceiptName
        };
        if (Directory.EnumerateFileSystemEntries(backup)
                .Any(item => !expected.Contains(Path.GetFileName(item))))
            throw new InvalidDataException(
                "Unexpected files in the Caustica experimental backup. Refusing to modify or delete them.");

        if (File.Exists(original) || !Sha256(backedUp).Equals(receipt.OriginalSha256, StringComparison.OrdinalIgnoreCase)
            || !Sha256(experimental).Equals(receipt.ExperimentalSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Caustica JARs changed since import. Refusing an unsafe overwrite.");

        var temporary = Path.Combine(mods, ".caustica-restore-" + Guid.NewGuid().ToString("N") + ".tmp");
        var savedExperiment = Path.Combine(backup, "experimental.jar");
        if (File.Exists(savedExperiment))
            throw new IOException("Experimental restore backup already exists.");

        try
        {
            // If ReSTIR was enabled, turn it off before restoring the stable JAR.
            var state = _restir.Inspect(root);
            if (state.Available && state.Enabled)
                _restir.SetEnabled(root, false);

            File.Copy(backedUp, temporary, overwrite: false);
            File.Move(experimental, savedExperiment);
            try
            {
                File.Move(temporary, original);
            }
            catch
            {
                File.Move(savedExperiment, experimental);
                throw;
            }

            // Remove only manager-owned files; never recursively delete an
            // instance backup directory containing unexpected user data.
            File.Delete(backedUp);
            File.Delete(savedExperiment);
            File.Delete(receiptPath);
            Directory.Delete(backup, recursive: false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private void ValidatePinnedJar(string jarPath)
    {
        var info = new FileInfo(jarPath);
        if (info.Length is < 1024 or > 64 * 1024 * 1024
            || !Sha256(jarPath).Equals(_approvedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unapproved Caustica experimental JAR SHA-256.");

        using var zip = ZipFile.OpenRead(jarPath);
        var manifest = zip.GetEntry("fabric.mod.json");
        if (manifest is null || manifest.Length is <= 0 or > 131072)
            throw new InvalidDataException("Missing Caustica Fabric metadata.");

        using (var stream = manifest.Open())
        using (var metadata = JsonDocument.Parse(stream))
        {
            var root = metadata.RootElement;
            if (!root.TryGetProperty("id", out var id) || id.GetString() != "caustica"
                || !root.TryGetProperty("depends", out var depends)
                || !depends.TryGetProperty("minecraft", out var version)
                || version.GetString() != MinecraftIntegrationService.MinecraftVersion)
                throw new InvalidDataException("Caustica JAR metadata is incompatible.");
        }

        var feature = zip.GetEntry("dev/comfyfluffy/caustica/CausticaConfig$Rt$Lights.class");
        if (feature is null || feature.Length is <= 0 or > 262144)
            throw new InvalidDataException("Missing experimental ReSTIR configuration.");
        using (var reader = new BinaryReader(feature.Open(), Encoding.UTF8))
        {
            var bytes = reader.ReadBytes((int)feature.Length);
            var text = Encoding.UTF8.GetString(bytes);
            if (!text.Contains("lights.restir-di", StringComparison.Ordinal)
                || !text.Contains("caustica.rt.restirDi", StringComparison.Ordinal))
                throw new InvalidDataException("Experimental ReSTIR markers are missing.");
        }

        foreach (var name in new[]
                 {
                     "caustica/natives/windows-x64/ngxshim.dll",
                     "caustica/natives/windows-x64/nvngx_dlssd.dll",
                     "caustica/natives/windows-x64/nvngx_dlssg.dll"
                 })
        {
            if (zip.GetEntry(name)?.Length is not > 0)
                throw new InvalidDataException("Missing required Windows RTX native component: " + name);
        }
    }

    private static void RequirePlainDirectory(string path)
    {
        if (!Directory.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A required Minecraft directory is missing or linked: " + path);
    }

    private static void RequirePlainDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
            RequirePlainDirectory(path);
        else if (File.Exists(path))
            throw new IOException("The backup parent is not a directory.");
    }

    private static void RequirePlainFile(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A required Minecraft file is missing or linked: " + path);
    }

    private static string BackupDirectory(string root) =>
        Path.Combine(Path.GetFullPath(root), ".dlss-nr-manager-backups", BackupFolder);

    private static string ReceiptPath(string root) =>
        Path.Combine(BackupDirectory(root), ReceiptName);

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record Receipt(
        string OriginalName,
        string OriginalSha256,
        string ExperimentalName,
        string ExperimentalSha256,
        string SourceCommit,
        string DlssSdkVersion);
}
