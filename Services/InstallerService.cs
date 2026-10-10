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
    private const long MaxExtractedArchiveBytes = 4L * 1024 * 1024 * 1024;
    private const int MaxArchiveEntries = 100_000;
    private static readonly string[] ProxyNames =
    [
        "dbghelp.dll", "dxgi.dll", "d3d12.dll", "winmm.dll",
        "version.dll", "wininet.dll", "winhttp.dll"
    ];

    public InstallState Inspect(string gameDir, string gpuGeneration)
    {
        RecoverInterruptedTransaction(gameDir);

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
            hash = HashService.Sha256(runtime);

        var expected = ExpectedRuntimeHash(gpuGeneration);
        var manifest = ReadManifest(gameDir);
        var runtimeHashValid =
            hash != null &&
            (
                (expected != null &&
                 hash.Equals(expected, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(manifest?.RuntimeHash) &&
                 hash.Equals(manifest.RuntimeHash, StringComparison.OrdinalIgnoreCase))
            );

        var ini = Path.Combine(gameDir, "OptiScaler.ini");
        var forwarder = Path.Combine(gameDir, "nvngx.dll_dlssnr.dll");
        var optiFolder = Path.Combine(gameDir, "OptiScaler");
        var log = Path.Combine(gameDir, "OptiScaler.log");
        var versionMarker = Path.Combine(gameDir, ".dlssnr-manager-version");
        var proxyMarker = Path.Combine(gameDir, ".dlssnr-manager-proxy");
        var manifestPath = Path.Combine(gameDir, ManifestFile);

        var iniPresent = File.Exists(ini);
        var runtimePresent = File.Exists(runtime);
        var forwarderPresent = File.Exists(forwarder);

        var managerEvidence = File.Exists(versionMarker) ||
                              File.Exists(proxyMarker) ||
                              File.Exists(manifestPath);

        var optiEvidence = Directory.Exists(optiFolder) ||
                           File.Exists(log) ||
                           proxy != null;

        // Current installs are identified by the managed proxy/manifest and INI. Neural Rendering
        // runtime presence is optional on RTX 20/30/40, where the manager installs only supported DLSS
        // features. Legacy DLSS-NR installs remain recognized by their runtime + manager markers.
        var currentInstall = iniPresent &&
                             (proxy != null || forwarderPresent) &&
                             (managerEvidence || optiEvidence);

        var legacyManagerInstall = iniPresent &&
                                   runtimePresent &&
                                   managerEvidence;

        var installed = currentInstall || legacyManagerInstall;

        return new(
            installed,
            proxy,
            ReadInstalledVersion(gameDir),
            File.Exists(runtime),
            hash,
            runtimeHashValid);
    }

    public async Task<string> InstallAsync(
        string gameDir,
        string? runtimePath,
        GpuInfo gpu,
        ReleaseInfo release,
        string proxy,
        string workingScale,
        GitHubReleaseService releases,
        bool enableNeuralRendering)
    {
        var gameExe = FindMainExecutable(gameDir)
            ?? throw new InvalidOperationException("No game executable was found in the selected target folder.");

        var capabilities = GpuCapabilityService.Evaluate(gpu);
        if (!capabilities.IsSupportedRtx)
            throw new InvalidOperationException(capabilities.BlockingReason ?? "Unsupported NVIDIA RTX GPU.");

        RuntimeValidation? validation = null;
        if (enableNeuralRendering)
        {
            if (!capabilities.NeuralRendering)
                throw new InvalidOperationException("DLSS Neural Rendering requires a supported RTX 50 Series GPU.");

            if (string.IsNullOrWhiteSpace(runtimePath) || !File.Exists(runtimePath))
                throw new InvalidOperationException("A validated NVIDIA DLSS Neural Rendering runtime is required on RTX 50.");

            var expected = ExpectedRuntimeHash(gpu.Generation)
                ?? throw new InvalidOperationException("Unsupported or undetected NVIDIA RTX generation.");

            validation = await RuntimeValidationService.ValidateAsync(runtimePath, gpu.Generation);

            var knownHash =
                validation.Hash.Equals(expected, StringComparison.OrdinalIgnoreCase);

            var trustedNvidiaSignedRuntime =
                validation.Is64Bit &&
                validation.SignatureValid &&
                !string.IsNullOrWhiteSpace(validation.Publisher) &&
                validation.Publisher.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);

            if (!knownHash && !trustedNvidiaSignedRuntime)
            {
                throw new InvalidOperationException(
                    "DLSSNR runtime validation failed. The runtime must either match a known validated SHA-256 " +
                    "or be a trusted x64 NVIDIA-signed runtime from the official Streamline package. " +
                    $"SHA-256: {validation.Hash}; Publisher: {validation.Publisher ?? "unknown"}.");
            }

            if (!validation.Is64Bit)
                throw new InvalidOperationException("The selected DLSSNR runtime is not a 64-bit PE DLL.");
        }

        var proxyPath = Path.Combine(gameDir, proxy);
        var managedProxy = ReadManagedProxy(gameDir);
        var proxyOwnedByManager = !string.IsNullOrWhiteSpace(managedProxy) &&
                                  proxy.Equals(managedProxy, StringComparison.OrdinalIgnoreCase);

        if (File.Exists(proxyPath) && !proxyOwnedByManager && !IsOptiScaler(proxyPath))
            throw new InvalidOperationException(
                $"{proxy} already exists and is not identified as OptiScaler. Choose another proxy or resolve loader chaining first.");

        var previousManifest = ReadManifest(gameDir);
        var backup = CreateBackup(gameDir);
        var baselineBackup = !string.IsNullOrWhiteSpace(previousManifest?.BaselineBackup)
            ? previousManifest!.BaselineBackup!
            : Path.GetRelativePath(gameDir, backup);

        var journal = FileTransactionJournal.Begin(
            gameDir,
            previousManifest == null ? "install" : "update",
            backup);
        journal.Stage("BACKED_UP");

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

            // The original asset is immutable by ID, SHA-256 and byte size.
            // Never install a different same-version rebuilt archive.
            if (OptiScalerLegacyReleasePolicy.IsPinnedCandidate(release) &&
                new FileInfo(zip).Length != OptiScalerLegacyReleasePolicy.ArchiveSize)
            {
                throw new InvalidDataException(
                    "Verified original OptiScaler ZIP size differs from the pinned release.");
            }

            var extract = Path.Combine(temp, "extract");
            ExtractSafe(zip, extract);

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

            var previouslyManaged = new HashSet<string>(
                previousManifest?.ManagedFiles ?? [],
                StringComparer.OrdinalIgnoreCase);

            var preservedUserOwnedNvidia = archiveRelativeFiles
                .Where(relative =>
                    !previouslyManaged.Contains(relative) &&
                    ShouldPreserveNewerDestination(
                        sourceRoot,
                        gameDir,
                        relative))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            journal.Stage("WRITING");

            foreach (var source in Directory.EnumerateFiles(
                         sourceRoot,
                         "*",
                         SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceRoot, source);
                if (!TryResolveUnderRoot(gameDir, relative, out var destination))
                    throw new InvalidDataException(
                        $"Unsafe archive destination: {relative}");

                journal.Track(destination);
            }

            CopyTreePreservingNewerNvidia(
                sourceRoot,
                gameDir,
                overwrite: true);

            var optiDll = Path.Combine(gameDir, "OptiScaler.dll");
            journal.Track(proxyPath);
            File.Copy(optiDll, proxyPath, true);
            File.Delete(optiDll);

            if (enableNeuralRendering && runtimePath != null)
            {
                journal.Track(runtimeDestination);
                CopyIfNotOlder(runtimePath, runtimeDestination);
            }

            IniService.ApplyPreset(
                Path.Combine(gameDir, "OptiScaler.ini"),
                workingScale,
                enableNeuralRendering);

            journal.Track(versionMarkerPath);
            journal.Track(proxyMarkerPath);
            journal.Track(manifestPath);

            AtomicFile.WriteAllText(
                versionMarkerPath,
                release.Tag);
            AtomicFile.WriteAllText(
                proxyMarkerPath,
                proxy);

            var managedFiles = archiveRelativeFiles
                .Where(x =>
                    !x.Equals(
                        "OptiScaler.dll",
                        StringComparison.OrdinalIgnoreCase) &&
                    !preservedUserOwnedNvidia.Contains(x))
                .Concat(new[]
                {
                    proxy,
                    ".dlssnr-manager-version",
                    ".dlssnr-manager-proxy",
                    ManifestFile
                })
                .Concat(enableNeuralRendering ? new[] { "nvngx_dlssnr.dll" } : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var managedFileHashes = await BuildManagedFileHashesAsync(
                gameDir,
                managedFiles,
                cancellationToken: default);

            var manifest = new InstallManifest(
                release.Tag,
                proxy,
                Path.GetFileName(gameExe),
                await HashService.Sha256Async(gameExe),
                validation?.Hash ?? "",
                DateTimeOffset.UtcNow,
                managedFiles,
                baselineBackup,
                managedFileHashes,
                release.ZipUrl,
                release.ZipSha256);

            journal.Stage("VERIFIED");

            AtomicFile.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions { WriteIndented = true }));

            journal.Commit();

            GameHistoryService.Append(
                gameDir,
                previousManifest == null ? "Install" : "Update",
                $"OptiScaler {release.Tag} • proxy {proxy} • SHA-256 {release.ZipSha256 ?? "unknown"}",
                Path.GetRelativePath(gameDir, backup));

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
            FileTransactionJournal.ClearPending(gameDir);
            GameHistoryService.Append(
                gameDir,
                "Rollback",
                $"Failed {release.Tag} install/update rolled back.",
                Path.GetRelativePath(gameDir, backup));
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

    public void RegisterManagedFiles(
        string gameDir,
        IEnumerable<string> absolutePaths)
    {
        var manifestPath = Path.Combine(gameDir, ManifestFile);
        var manifest = ReadManifest(gameDir)
            ?? throw new InvalidOperationException(
                "Cannot register managed files because the install manifest is missing.");

        var managed = new HashSet<string>(
            manifest.ManagedFiles ?? [],
            StringComparer.OrdinalIgnoreCase);

        var newlyRegistered = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var path in absolutePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            string relative;
            try
            {
                relative = Path.GetRelativePath(
                    gameDir,
                    Path.GetFullPath(path));
            }
            catch
            {
                continue;
            }

            if (!TryResolveUnderRoot(
                    gameDir,
                    relative,
                    out var resolved) ||
                !File.Exists(resolved))
            {
                continue;
            }

            if (managed.Add(relative))
                newlyRegistered.Add(relative);
        }

        var managedList = managed
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hashes = new Dictionary<string, string>(
            manifest.ManagedFileHashes ??
            new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        // Re-hash only newly registered files and legacy tracked files that
        // do not yet have an integrity fingerprint. Existing hashes remain
        // stable until an explicit managed update replaces those files.
        foreach (var relative in managedList.Where(relative =>
                     newlyRegistered.Contains(relative) ||
                     !hashes.ContainsKey(relative)))
        {
            if (relative.Equals(
                    ManifestFile,
                    StringComparison.OrdinalIgnoreCase) ||
                !TryResolveUnderRoot(gameDir, relative, out var file) ||
                !File.Exists(file))
            {
                continue;
            }

            hashes[relative] = HashService.Sha256(file);
        }

        var updated = manifest with
        {
            ManagedFiles = managedList,
            ManagedFileHashes = hashes
        };

        AtomicFile.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(
                updated,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    public void ApplyPreset(string gameDir, string workingScale, bool enableNeuralRendering = true)
    {
        var ini = Path.Combine(gameDir, "OptiScaler.ini");
        if (!File.Exists(ini))
            throw new InvalidOperationException("OptiScaler.ini was not found for the selected game.");

        IniService.ApplyPreset(ini, workingScale, enableNeuralRendering);
        RefreshManagedFileHash(gameDir, "OptiScaler.ini");
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

        RefreshManagedFileHash(gameDir, "OptiScaler.ini");
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
        GameHistoryService.Append(
            gameDir,
            "Restore",
            $"Restored backup {Path.GetFileName(latest)}.",
            Path.GetRelativePath(gameDir, latest));
    }

    public void Uninstall(string gameDir, bool preserveBackups = true)
    {
        var manifest = ReadManifest(gameDir);
        if (manifest?.ManagedFiles is { Count: > 0 })
        {
            foreach (var relative in manifest.ManagedFiles)
            {
                if (!TryResolveUnderRoot(gameDir, relative, out var file))
                    continue;

                if (File.Exists(file))
                    File.Delete(file);
            }

            RemoveEmptyManagedDirectories(gameDir, manifest.ManagedFiles);

            if (!string.IsNullOrWhiteSpace(manifest.BaselineBackup) &&
                TryResolveUnderRoot(
                    gameDir,
                    manifest.BaselineBackup,
                    out var baseline) &&
                Directory.Exists(baseline))
            {
                CopyTree(baseline, gameDir, true);
            }

            if (!preserveBackups)
            {
                var backups = Path.Combine(gameDir, ".dlssnr-manager-backups");
                if (Directory.Exists(backups))
                    Directory.Delete(backups, true);
            }

            return;
        }

        var backupsRoot = Path.Combine(
            gameDir,
            ".dlssnr-manager-backups");
        var managerEvidence =
            File.Exists(Path.Combine(gameDir, ".dlssnr-manager-version")) ||
            File.Exists(Path.Combine(gameDir, ".dlssnr-manager-proxy")) ||
            Directory.Exists(backupsRoot);

        if (!managerEvidence)
        {
            throw new InvalidOperationException(
                "No DLSS NR Manager manifest, marker or backup was found. " +
                "Refusing destructive legacy uninstall because the detected OptiScaler files may belong to a manual installation.");
        }

        if (preserveBackups)
            _ = CreateBackup(gameDir);

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
            var preferred =
                RendererDetectionService.ReadPreferredExecutable(gameDir);
            if (preferred != null)
                return preferred;

            var detected =
                RendererDetectionService.Detect(gameDir).Preferred?.Executable;
            if (detected != null)
                return detected;

            return Directory.EnumerateFiles(
                    gameDir,
                    "*.exe",
                    SearchOption.TopDirectoryOnly)
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

    private static void ExtractSafe(string zipPath, string destination)
        => SafeZip.Extract(zipPath, destination);

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
            if (!TryResolveUnderRoot(gameDir, relative, out var destination) ||
                !TryResolveUnderRoot(backup, relative, out var backupFile))
                continue;

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

    private static bool TryResolveUnderRoot(
        string rootPath,
        string relative,
        out string resolved)
    {
        resolved = string.Empty;

        if (string.IsNullOrWhiteSpace(relative) ||
            Path.IsPathRooted(relative))
            return false;

        try
        {
            var root = Path.GetFullPath(rootPath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            var candidate = Path.GetFullPath(
                Path.Combine(root, relative));

            if (!candidate.StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            resolved = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveEmptyManagedDirectories(string gameDir, IEnumerable<string> managedFiles)
    {
        var directories = managedFiles
            .Select(relative =>
                TryResolveUnderRoot(gameDir, relative, out var path)
                    ? Path.GetDirectoryName(path)
                    : null)
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

    private static void RefreshManagedFileHash(
        string gameDir,
        string relative)
    {
        var manifest = ReadManifest(gameDir);
        if (manifest?.ManagedFiles == null ||
            !manifest.ManagedFiles.Contains(
                relative,
                StringComparer.OrdinalIgnoreCase) ||
            !TryResolveUnderRoot(gameDir, relative, out var path) ||
            !File.Exists(path))
        {
            return;
        }

        var hashes = new Dictionary<string, string>(
            manifest.ManagedFileHashes ??
            new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase)
        {
            [relative] = HashService.Sha256(path)
        };

        var updated = manifest with
        {
            ManagedFileHashes = hashes
        };

        AtomicFile.WriteAllText(
            Path.Combine(gameDir, ManifestFile),
            JsonSerializer.Serialize(
                updated,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<IReadOnlyDictionary<string, string>> BuildManagedFileHashesAsync(
        string gameDir,
        IEnumerable<string> managedFiles,
        CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var relative in managedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (relative.Equals(
                    ManifestFile,
                    StringComparison.OrdinalIgnoreCase) ||
                !TryResolveUnderRoot(gameDir, relative, out var path) ||
                !File.Exists(path))
            {
                continue;
            }

            hashes[relative] =
                await HashService.Sha256Async(
                    path,
                    cancellationToken);
        }

        return hashes;
    }

    private static void RecoverInterruptedTransaction(string gameDir)
    {
        var pending = FileTransactionJournal.ReadPending(gameDir);
        if (pending == null || pending.Stage.Equals("COMMITTED", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (!Directory.Exists(pending.BackupDirectory))
            {
                AppLogger.Warn(
                    $"Interrupted transaction {pending.Id} has no backup directory; leaving journal for diagnostics.");
                return;
            }

            foreach (var relative in pending.Files)
            {
                if (!TryResolveUnderRoot(gameDir, relative, out var destination) ||
                    !TryResolveUnderRoot(pending.BackupDirectory, relative, out var backupFile))
                    continue;

                if (File.Exists(backupFile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(backupFile, destination, true);
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }

            FileTransactionJournal.ClearPending(gameDir);
            GameHistoryService.Append(
                gameDir,
                "Recovery",
                $"Recovered interrupted {pending.Operation} transaction {pending.Id}.",
                Path.GetRelativePath(gameDir, pending.BackupDirectory));

            AppLogger.Warn(
                $"Recovered interrupted game transaction {pending.Id} at stage {pending.Stage}.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                $"Interrupted transaction recovery failed: {ex.Message}");
        }
    }

    private static bool ShouldPreserveNewerDestination(
        string sourceRoot,
        string gameDir,
        string relative)
    {
        if (!IsNvidiaDlssRuntime(
                Path.GetFileName(relative)) ||
            !TryResolveUnderRoot(
                sourceRoot,
                relative,
                out var source) ||
            !TryResolveUnderRoot(
                gameDir,
                relative,
                out var destination) ||
            !File.Exists(source) ||
            !File.Exists(destination))
        {
            return false;
        }

        return IsDestinationNewer(
            destination,
            source);
    }

    private static void CopyTreePreservingNewerNvidia(
        string source,
        string dest,
        bool overwrite)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
        {
            var destination = Path.Combine(dest, Path.GetFileName(file));

            if (IsNvidiaDlssRuntime(Path.GetFileName(file)) &&
                File.Exists(destination) &&
                IsDestinationNewer(destination, file))
            {
                AppLogger.Info(
                    $"Preserved newer NVIDIA runtime: {Path.GetFileName(destination)}.");
                continue;
            }

            File.Copy(file, destination, overwrite);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyTreePreservingNewerNvidia(
                dir,
                Path.Combine(dest, Path.GetFileName(dir)),
                overwrite);
        }
    }

    private static void CopyIfNotOlder(
        string source,
        string destination)
    {
        if (File.Exists(destination) &&
            IsDestinationNewer(destination, source))
        {
            AppLogger.Info(
                $"Preserved newer destination runtime: {Path.GetFileName(destination)}.");
            return;
        }

        File.Copy(source, destination, true);
    }

    private static bool IsNvidiaDlssRuntime(string name)
        => name.StartsWith("nvngx_dlss", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("sl.dlss", StringComparison.OrdinalIgnoreCase)
           || name.Equals("sl.interposer.dll", StringComparison.OrdinalIgnoreCase)
           || name.Equals("sl.common.dll", StringComparison.OrdinalIgnoreCase)
           || name.Equals("sl.reflex.dll", StringComparison.OrdinalIgnoreCase);

    private static bool IsDestinationNewer(
        string destination,
        string source)
    {
        try
        {
            var destinationVersion =
                FileVersionInfo.GetVersionInfo(destination).FileVersion;
            var sourceVersion =
                FileVersionInfo.GetVersionInfo(source).FileVersion;

            return Version.TryParse(
                       NormalizeFileVersion(destinationVersion),
                       out var current) &&
                   Version.TryParse(
                       NormalizeFileVersion(sourceVersion),
                       out var incoming) &&
                   current > incoming;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeFileVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "0.0.0.0";

        var numeric = new string(
            value.TakeWhile(ch =>
                char.IsDigit(ch) || ch == '.')
                .ToArray())
            .Trim('.');

        return string.IsNullOrWhiteSpace(numeric)
            ? "0.0.0.0"
            : numeric;
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
