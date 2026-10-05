using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

public sealed class InstallerService
{
    public static readonly string Rtx50Hash = "E16BCF15E16E13F527491CDF7845B2FE6521A738D8F7C9C721866A8496E1FC8E";
    public static readonly string Rtx2040Hash = "E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A";

    private const string ManifestFile = ".dlssnr-manager-state.json";
    private static readonly string[] ProxyNames =
    [
        "dbghelp.dll", "dxgi.dll", "d3d12.dll", "winmm.dll",
        "version.dll", "wininet.dll", "winhttp.dll"
    ];

    public InstallState Inspect(string gameDir, string gpuGeneration)
    {
        var managedProxy = ReadManagedProxy(gameDir);
        string? proxy = null;

        if (!string.IsNullOrWhiteSpace(managedProxy) &&
            ProxyNames.Contains(managedProxy, StringComparer.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(gameDir, managedProxy)))
        {
            proxy = managedProxy;
        }

        proxy ??= ProxyNames.FirstOrDefault(p => IsOptiScaler(Path.Combine(gameDir, p)));
        proxy ??= InferProxyFromLog(gameDir);

        var runtime = Path.Combine(gameDir, "nvngx_dlssnr.dll");
        string? hash = null;
        if (File.Exists(runtime))
            hash = HashService.Sha256Async(runtime).GetAwaiter().GetResult();

        var expected = ExpectedRuntimeHash(gpuGeneration);

        var ini = Path.Combine(gameDir, "OptiScaler.ini");
        var forwarder = Path.Combine(gameDir, "nvngx.dll_dlssnr.dll");
        var optiFolder = Path.Combine(gameDir, "OptiScaler");
        var log = Path.Combine(gameDir, "OptiScaler.log");
        var versionMarker = Path.Combine(gameDir, ".dlssnr-manager-version");

        var corePresent = File.Exists(ini) &&
                          File.Exists(runtime) &&
                          File.Exists(forwarder);

        var legacyEvidence = Directory.Exists(optiFolder) ||
                             File.Exists(log) ||
                             File.Exists(versionMarker) ||
                             File.Exists(Path.Combine(gameDir, ManifestFile));

        var installed = corePresent && (proxy != null || legacyEvidence);

        return new(
            installed,
            proxy,
            ReadInstalledVersion(gameDir),
            File.Exists(runtime),
            hash,
            expected != null && hash?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true);
    }

    public async Task<string> InstallAsync(
        string gameDir,
        string runtimePath,
        GpuInfo gpu,
        ReleaseInfo release,
        string proxy,
        string workingScale,
        GitHubReleaseService releases)
    {
        var gameExe = FindMainExecutable(gameDir)
            ?? throw new InvalidOperationException("No game executable was found in the selected target folder.");

        var expected = ExpectedRuntimeHash(gpu.Generation)
            ?? throw new InvalidOperationException("Unsupported or undetected NVIDIA RTX generation.");

        var validation = await RuntimeValidationService.ValidateAsync(runtimePath, gpu.Generation);
        if (!validation.Hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"DLSSNR runtime SHA-256 mismatch. Expected {expected}, got {validation.Hash}.");

        if (!validation.Is64Bit)
            throw new InvalidOperationException("The selected DLSSNR runtime is not a 64-bit PE DLL.");

        var proxyPath = Path.Combine(gameDir, proxy);
        var managedProxy = ReadManagedProxy(gameDir);
        var proxyOwnedByManager = !string.IsNullOrWhiteSpace(managedProxy) &&
                                  proxy.Equals(managedProxy, StringComparison.OrdinalIgnoreCase);

        if (File.Exists(proxyPath) && !proxyOwnedByManager && !IsOptiScaler(proxyPath))
            throw new InvalidOperationException(
                $"{proxy} already exists and is not identified as OptiScaler. Choose another proxy or resolve loader chaining first.");

        var backup = CreateBackup(gameDir);
        var temp = Path.Combine(Path.GetTempPath(), "DlssNrManager", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        var archiveRelativeFiles = new List<string>();
        var existingArchiveDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var proxyExistedBefore = File.Exists(proxyPath);
        var runtimeDestination = Path.Combine(gameDir, "nvngx_dlssnr.dll");
        var runtimeExistedBefore = File.Exists(runtimeDestination);
        var versionMarkerPath = Path.Combine(gameDir, ".dlssnr-manager-version");
        var proxyMarkerPath = Path.Combine(gameDir, ".dlssnr-manager-proxy");
        var manifestPath = Path.Combine(gameDir, ManifestFile);
        var versionMarkerExistedBefore = File.Exists(versionMarkerPath);
        var proxyMarkerExistedBefore = File.Exists(proxyMarkerPath);
        var manifestExistedBefore = File.Exists(manifestPath);

        try
        {
            var zip = Path.Combine(temp, "optiscaler.zip");
            await releases.DownloadAsync(release.ZipUrl, zip, release.ZipSha256);

            var extract = Path.Combine(temp, "extract");
            ZipFile.ExtractToDirectory(zip, extract);

            var sourceRoot = Directory.GetFiles(extract, "OptiScaler.dll", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault();

            if (sourceRoot == null)
                throw new InvalidDataException("Release archive does not contain OptiScaler.dll.");

            archiveRelativeFiles = Directory
                .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(sourceRoot, path))
                .ToList();

            foreach (var relative in archiveRelativeFiles)
            {
                if (File.Exists(Path.Combine(gameDir, relative)))
                    existingArchiveDestinations.Add(relative);
            }

            BackupDestinationCollisions(sourceRoot, gameDir, backup);
            CopyTree(sourceRoot, gameDir, overwrite: true);

            var optiDll = Path.Combine(gameDir, "OptiScaler.dll");
            File.Copy(optiDll, proxyPath, true);
            File.Delete(optiDll);

            File.Copy(runtimePath, Path.Combine(gameDir, "nvngx_dlssnr.dll"), true);
            IniService.ApplyPreset(Path.Combine(gameDir, "OptiScaler.ini"), workingScale);

            File.WriteAllText(Path.Combine(gameDir, ".dlssnr-manager-version"), release.Tag);
            File.WriteAllText(Path.Combine(gameDir, ".dlssnr-manager-proxy"), proxy);

            var managedFiles = archiveRelativeFiles
                .Where(x => !x.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
                .Concat(new[]
                {
                    proxy,
                    "nvngx_dlssnr.dll",
                    ".dlssnr-manager-version",
                    ".dlssnr-manager-proxy",
                    ManifestFile
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var manifest = new InstallManifest(
                release.Tag,
                proxy,
                Path.GetFileName(gameExe),
                await HashService.Sha256Async(gameExe),
                validation.Hash,
                DateTimeOffset.UtcNow,
                managedFiles);

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            return backup;
        }
        catch
        {
            RollbackFailedInstall(
                gameDir,
                backup,
                archiveRelativeFiles,
                existingArchiveDestinations,
                proxy,
                proxyExistedBefore,
                runtimeExistedBefore,
                versionMarkerExistedBefore,
                proxyMarkerExistedBefore,
                manifestExistedBefore);
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp))
                    Directory.Delete(temp, true);
            }
            catch { }
        }
    }

    public void ApplyPreset(string gameDir, string workingScale)
    {
        var ini = Path.Combine(gameDir, "OptiScaler.ini");
        if (!File.Exists(ini))
            throw new InvalidOperationException("OptiScaler.ini was not found for the selected game.");

        IniService.ApplyPreset(ini, workingScale);
    }

    public void ApplyAdvancedSettings(
        string gameDir,
        int fpsType,
        int fpsPosition,
        bool showFps,
        string? targetProcessName,
        bool loadReShade)
    {
        var ini = Path.Combine(gameDir, "OptiScaler.ini");
        if (!File.Exists(ini))
            throw new InvalidOperationException("OptiScaler.ini was not found for the selected game.");

        IniService.ApplyAdvancedSettings(
            ini,
            fpsType,
            fpsPosition,
            showFps,
            targetProcessName,
            loadReShade);
    }

    public string CreateBackup(string gameDir)
    {
        var root = Path.Combine(gameDir, ".dlssnr-manager-backups");
        Directory.CreateDirectory(root);

        var folder = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var suffix = 0;
        while (Directory.Exists(folder))
            folder = Path.Combine(root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{++suffix}");

        Directory.CreateDirectory(folder);

        foreach (var name in ProxyNames.Concat(
                     new[]
                     {
                         "OptiScaler.ini", "OptiScaler.log", "nvngx_dlssnr.dll",
                         "nvngx.dll_dlssnr.dll", ".dlssnr-manager-version",
                         ".dlssnr-manager-proxy", ManifestFile
                     }))
        {
            var src = Path.Combine(gameDir, name);
            if (File.Exists(src))
                CopyIntoBackup(gameDir, src, folder);
        }

        var opti = Path.Combine(gameDir, "OptiScaler");
        if (Directory.Exists(opti))
            CopyTree(opti, Path.Combine(folder, "OptiScaler"), true);

        return folder;
    }

    public void RestoreLatest(string gameDir)
    {
        var root = Path.Combine(gameDir, ".dlssnr-manager-backups");
        var latest = Directory.Exists(root)
            ? Directory.GetDirectories(root).OrderByDescending(x => x).FirstOrDefault()
            : null;

        if (latest == null)
            throw new InvalidOperationException("No backup available.");

        Uninstall(gameDir, preserveBackups: true);
        CopyTree(latest, gameDir, true);
    }

    public void Uninstall(string gameDir, bool preserveBackups = true)
    {
        var manifest = ReadManifest(gameDir);
        if (manifest?.ManagedFiles is { Count: > 0 })
        {
            foreach (var relative in manifest.ManagedFiles)
            {
                if (!IsSafeRelativePath(relative))
                    continue;

                var file = Path.Combine(gameDir, relative);
                if (File.Exists(file))
                    File.Delete(file);
            }

            RemoveEmptyManagedDirectories(gameDir, manifest.ManagedFiles);

            if (!preserveBackups)
            {
                var backups = Path.Combine(gameDir, ".dlssnr-manager-backups");
                if (Directory.Exists(backups))
                    Directory.Delete(backups, true);
            }

            return;
        }

        var managedProxy = ReadManagedProxy(gameDir);

        foreach (var p in ProxyNames)
        {
            var file = Path.Combine(gameDir, p);
            if (!File.Exists(file))
                continue;

            var isManaged = !string.IsNullOrWhiteSpace(managedProxy) &&
                            p.Equals(managedProxy, StringComparison.OrdinalIgnoreCase);

            if (isManaged || IsOptiScaler(file))
                File.Delete(file);
        }

        foreach (var name in new[]
                 {
                     "OptiScaler.ini", "OptiScaler.log", "nvngx_dlssnr.dll",
                     "nvngx.dll_dlssnr.dll", ".dlssnr-manager-version",
                     ".dlssnr-manager-proxy", ManifestFile
                 })
        {
            var file = Path.Combine(gameDir, name);
            if (File.Exists(file))
                File.Delete(file);
        }

        var opti = Path.Combine(gameDir, "OptiScaler");
        if (Directory.Exists(opti))
            Directory.Delete(opti, true);

        if (!preserveBackups)
        {
            var backups = Path.Combine(gameDir, ".dlssnr-manager-backups");
            if (Directory.Exists(backups))
                Directory.Delete(backups, true);
        }
    }

    public string ReadLog(string gameDir)
    {
        var path = Path.Combine(gameDir, "OptiScaler.log");
        if (!File.Exists(path))
            return "No OptiScaler.log found yet.";

        try
        {
            const int maxBytes = 1024 * 1024;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > maxBytes)
                stream.Seek(-maxBytes, SeekOrigin.End);

            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var text = reader.ReadToEnd();

            if (stream.Length > maxBytes)
            {
                var firstNewLine = text.IndexOf('\n');
                if (firstNewLine >= 0)
                    text = text[(firstNewLine + 1)..];
                text = "[... log truncated to last 1 MiB ...]\n" + text;
            }

            return text;
        }
        catch (Exception ex)
        {
            return $"Unable to read OptiScaler.log: {ex.Message}";
        }
    }

    public static InstallManifest? ReadManifest(string gameDir)
    {
        var path = Path.Combine(gameDir, ManifestFile);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static string? FindMainExecutable(string gameDir)
    {
        try
        {
            return Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly)
                .OrderByDescending(x => new FileInfo(x).Length)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? ExpectedRuntimeHash(string gpuGeneration)
        => gpuGeneration == "RTX 50"
            ? Rtx50Hash
            : gpuGeneration is "RTX 20" or "RTX 30" or "RTX 40"
                ? Rtx2040Hash
                : null;

    private static void RollbackFailedInstall(
        string gameDir,
        string backup,
        IReadOnlyList<string> archiveRelativeFiles,
        ISet<string> existingArchiveDestinations,
        string proxy,
        bool proxyExistedBefore,
        bool runtimeExistedBefore,
        bool versionMarkerExistedBefore,
        bool proxyMarkerExistedBefore,
        bool manifestExistedBefore)
    {
        foreach (var relative in archiveRelativeFiles)
        {
            if (!IsSafeRelativePath(relative))
                continue;

            var destination = Path.Combine(gameDir, relative);
            var backupFile = Path.Combine(backup, relative);

            try
            {
                if (existingArchiveDestinations.Contains(relative) && File.Exists(backupFile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(backupFile, destination, true);
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            catch { }
        }

        RestoreOrDeleteKnownFile(gameDir, backup, proxy, proxyExistedBefore);
        RestoreOrDeleteKnownFile(gameDir, backup, "nvngx_dlssnr.dll", runtimeExistedBefore);
        RestoreOrDeleteKnownFile(gameDir, backup, ".dlssnr-manager-version", versionMarkerExistedBefore);
        RestoreOrDeleteKnownFile(gameDir, backup, ".dlssnr-manager-proxy", proxyMarkerExistedBefore);
        RestoreOrDeleteKnownFile(gameDir, backup, ManifestFile, manifestExistedBefore);
    }

    private static void RestoreOrDeleteKnownFile(string gameDir, string backup, string relative, bool existedBefore)
    {
        var destination = Path.Combine(gameDir, relative);
        var backupFile = Path.Combine(backup, relative);

        try
        {
            if (existedBefore && File.Exists(backupFile))
                File.Copy(backupFile, destination, true);
            else if (!existedBefore && File.Exists(destination))
                File.Delete(destination);
        }
        catch { }
    }

    private static bool IsSafeRelativePath(string relative)
        => !string.IsNullOrWhiteSpace(relative)
           && !Path.IsPathRooted(relative)
           && !relative.StartsWith("..", StringComparison.Ordinal)
           && !relative.Contains($"{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static void RemoveEmptyManagedDirectories(string gameDir, IEnumerable<string> managedFiles)
    {
        var directories = managedFiles
            .Where(IsSafeRelativePath)
            .Select(relative => Path.GetDirectoryName(Path.Combine(gameDir, relative)))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => path!.Length)
            .ToList();

        foreach (var directory in directories)
        {
            if (directory == null || directory.Equals(gameDir, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch { }
        }
    }

    private static void BackupDestinationCollisions(string sourceRoot, string gameDir, string backup)
    {
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            var destination = Path.Combine(gameDir, relative);
            if (File.Exists(destination))
                CopyIntoBackup(gameDir, destination, backup);
        }
    }

    private static void CopyIntoBackup(string gameDir, string source, string backupRoot)
    {
        var relative = Path.GetRelativePath(gameDir, source);
        if (relative.StartsWith("..", StringComparison.Ordinal))
            return;

        var destination = Path.Combine(backupRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private static string? InferProxyFromLog(string gameDir)
    {
        var path = Path.Combine(gameDir, "OptiScaler.log");
        if (!File.Exists(path))
            return null;

        try
        {
            foreach (var line in File.ReadLines(path).Take(400))
            {
                foreach (var proxy in ProxyNames)
                {
                    if (line.Contains(proxy, StringComparison.OrdinalIgnoreCase))
                        return proxy;
                }
            }
        }
        catch { }

        return null;
    }

    private static string? ReadInstalledVersion(string gameDir)
    {
        var path = Path.Combine(gameDir, ".dlssnr-manager-version");
        if (File.Exists(path))
            return File.ReadAllText(path).Trim();

        return ReadManifest(gameDir)?.Release;
    }

    private static string? ReadManagedProxy(string gameDir)
    {
        var path = Path.Combine(gameDir, ".dlssnr-manager-proxy");
        if (File.Exists(path))
            return File.ReadAllText(path).Trim();

        return ReadManifest(gameDir)?.Proxy;
    }

    private static bool IsOptiScaler(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.Equals(info.OriginalFilename, "OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ||
                   (info.FileDescription?.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) ?? false);
        }
        catch
        {
            return false;
        }
    }

    private static void CopyTree(string source, string dest, bool overwrite)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite);

        foreach (var dir in Directory.GetDirectories(source))
            CopyTree(dir, Path.Combine(dest, Path.GetFileName(dir)), overwrite);
    }
}
