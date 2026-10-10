using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

public sealed class DiagnosticService
{
    private static readonly string[] TemporalSignals =
    [
        "nvngx_dlss.dll",
        "nvngx_dlssd.dll",
        "sl.interposer.dll",
        "libxess.dll",
        "amd_fidelityfx_dx12.dll",
        "amd_fidelityfx_loader_dx12.dll"
    ];

    private static readonly string[] LoaderSignals =
    [
        "ReShade64.dll",
        "dxgi.dll",
        "d3d11.dll",
        "d3d12.dll",
        "version.dll",
        "winmm.dll",
        "wininet.dll",
        "winhttp.dll",
        "dbghelp.dll"
    ];

    public DiagnosticResult Diagnose(string gameDir, GpuInfo gpu, InstallState state)
    {
        var lines = new List<string>();
        var exe = FindMainExecutable(gameDir);
        lines.Add($"Game executable: {(exe == null ? "not found" : Path.GetFileName(exe))}");
        lines.Add($"GPU: {gpu.Name} • {gpu.Generation}");

        var temporal = TemporalSignals
            .Where(x => SafeFind(gameDir, x))
            .ToList();

        lines.Add(temporal.Count == 0
            ? "Temporal upscaler signals: none found"
            : $"Temporal upscaler signals: {string.Join(", ", temporal)}");

        lines.Add($"OptiScaler installation: {(state.Installed ? "installed" : "not installed")}");
        lines.Add($"Proxy: {state.ProxyName ?? "unknown"}");

        var capabilities = GpuCapabilityService.Evaluate(gpu);
        var nrValue = IniService.ReadValue(
            Path.Combine(gameDir, "OptiScaler.ini"),
            "DlssNr",
            "Enabled");
        var nrConfigured =
            bool.TryParse(nrValue, out var parsedNrEnabled) &&
            parsedNrEnabled;
        var nrRuntimeRequired =
            capabilities.NeuralRendering && nrConfigured;

        lines.Add(
            !capabilities.NeuralRendering
                ? $"DLSSNR runtime: not required on {gpu.Generation}"
                : !nrConfigured
                    ? "DLSSNR runtime: Neural Rendering disabled in OptiScaler.ini"
                    : state.RuntimePresent
                        ? $"DLSSNR runtime: {(state.RuntimeHashValid ? "validated installed runtime" : "present but unverified/changed")}"
                        : "DLSSNR runtime: missing");

        var logPath = Path.Combine(gameDir, "OptiScaler.log");
        var log = ReadTail(logPath, 500);
        var loaded = log.Any(x =>
            x.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) &&
            (x.Contains("loaded", StringComparison.OrdinalIgnoreCase) ||
             x.Contains("working as", StringComparison.OrdinalIgnoreCase) ||
             x.Contains("initializ", StringComparison.OrdinalIgnoreCase)));

        var nrRunning = log.Any(x =>
            x.Contains("DlssNr", StringComparison.OrdinalIgnoreCase) &&
            x.Contains("Running", StringComparison.OrdinalIgnoreCase))
            || log.Any(x =>
                x.Contains("Neural Rendering", StringComparison.OrdinalIgnoreCase) &&
                x.Contains("Running", StringComparison.OrdinalIgnoreCase));

        lines.Add($"OptiScaler loaded in game: {(loaded ? "yes" : "not confirmed")}");
        lines.Add($"DLSS Neural Rendering state: {(nrRunning ? "RUNNING" : "not confirmed")}");

        var reshadeManaged = IniService.ReadValue(
            Path.Combine(gameDir, "OptiScaler.ini"),
            "Plugins",
            "LoadReshade");

        var conflicts = DetectLoaderConflicts(
            gameDir,
            state.ProxyName,
            string.Equals(reshadeManaged, "true", StringComparison.OrdinalIgnoreCase));
        lines.Add(conflicts.Count == 0
            ? "Loader conflicts: none detected"
            : $"Potential loader conflicts: {string.Join(", ", conflicts)}");

        var manifestStatus = ReadManifestStatus(gameDir, exe);
        if (manifestStatus != null)
            lines.Add(manifestStatus);

        var runtimeReady =
            !nrRuntimeRequired ||
            (state.RuntimePresent && state.RuntimeHashValid);

        var ready = state.Installed &&
                    runtimeReady &&
                    temporal.Count > 0 &&
                    exe != null &&
                    conflicts.Count == 0;

        var summary = nrRunning
            ? "DLSS Neural Rendering is confirmed running."
            : ready && nrRuntimeRequired
                ? "Installation looks ready for Neural Rendering. Launch the game and verify the runtime reports Running."
                : ready
                    ? $"OptiScaler looks ready for the DLSS features supported by {gpu.Generation}; Neural Rendering is not required."
                    : "Configuration needs attention before the selected DLSS feature set can be considered ready.";

        return new(summary, lines, ready, loaded, nrRunning);
    }

    public string CreateSupportBundle(
        string gameDir,
        GpuInfo gpu,
        InstallState state,
        string destinationZip)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            throw new DirectoryNotFoundException("The selected game folder does not exist.");

        if (string.IsNullOrWhiteSpace(destinationZip))
            throw new ArgumentException("A destination ZIP path is required.", nameof(destinationZip));

        var destination = Path.GetFullPath(destinationZip);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("The diagnostics destination has no parent directory.");

        Directory.CreateDirectory(directory);

        var temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        var report = Diagnose(gameDir, gpu, state);

        try
        {
            using (var stream = new FileStream(
                       temp,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None))
            using (var archive = new ZipArchive(
                       stream,
                       ZipArchiveMode.Create,
                       leaveOpen: false))
            {
                var summary = new StringBuilder()
                    .AppendLine("DLSS NR Manager support bundle")
                    .AppendLine($"Created: {DateTimeOffset.Now:O}")
                    .AppendLine($"Version: {AppIdentity.VersionString}")
                    .AppendLine($"OS: {Environment.OSVersion.VersionString}")
                    .AppendLine($"Process architecture: {Environment.Is64BitProcess switch { true => "x64", false => "x86" }}")
                    .AppendLine()
                    .AppendLine(report.Summary)
                    .AppendLine()
                    .AppendJoin(
                        Environment.NewLine,
                        report.Lines.Select(line => "• " + line))
                    .ToString();

                AddTextEntry(
                    archive,
                    "diagnostic-report.txt",
                    SanitizeUserPaths(summary));

                AddFileTailIfPresent(
                    archive,
                    AppLogger.LogPath,
                    "logs/dlss-nr-manager.log",
                    2500);

                AddFileTailIfPresent(
                    archive,
                    AppLogger.PreviousLogPath,
                    "logs/dlss-nr-manager.previous.log",
                    2500);

                AddFileTailIfPresent(
                    archive,
                    Path.Combine(gameDir, "OptiScaler.log"),
                    "game/OptiScaler.log",
                    2500);

                var renderer = RendererDetectionService.Detect(gameDir);
                AddTextEntry(
                    archive,
                    "game/renderer-detection.txt",
                    SanitizeUserPaths(
                        renderer.Summary + Environment.NewLine +
                        string.Join(
                            Environment.NewLine,
                            renderer.Candidates.Select(candidate =>
                                $"{Path.GetFileName(candidate.Executable)} | {candidate.Api} | {candidate.Evidence}"))));

                var history = GameHistoryService.Read(gameDir);
                if (history.Count > 0)
                {
                    AddTextEntry(
                        archive,
                        "game/history.json",
                        SanitizeUserPaths(
                            JsonSerializer.Serialize(
                                history,
                                new JsonSerializerOptions { WriteIndented = true })));
                }

                var pending = FileTransactionJournal.ReadPending(gameDir);
                if (pending != null)
                {
                    AddTextEntry(
                        archive,
                        "game/pending-transaction.json",
                        SanitizeUserPaths(
                            JsonSerializer.Serialize(
                                pending,
                                new JsonSerializerOptions { WriteIndented = true })));
                }

                var iniPath = Path.Combine(gameDir, "OptiScaler.ini");
                if (File.Exists(iniPath))
                {
                    try
                    {
                        AddTextEntry(
                            archive,
                            "game/OptiScaler.ini",
                            SanitizeUserPaths(File.ReadAllText(iniPath)));
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Unable to add OptiScaler.ini to diagnostics bundle: {ex.Message}");
                    }
                }

                var manifest = InstallerService.ReadManifest(gameDir);
                if (manifest != null)
                {
                    AddTextEntry(
                        archive,
                        "game/install-manifest.json",
                        SanitizeUserPaths(
                            JsonSerializer.Serialize(
                                manifest,
                                new JsonSerializerOptions { WriteIndented = true })));
                }
            }

            File.Move(temp, destination, overwrite: true);
            return destination;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void AddFileTailIfPresent(
        ZipArchive archive,
        string path,
        string entryName,
        int maxLines)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var lines = ReadTail(path, maxLines);
            if (lines.Length == 0)
                return;

            AddTextEntry(
                archive,
                entryName,
                SanitizeUserPaths(string.Join(Environment.NewLine, lines)));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Unable to add {Path.GetFileName(path)} to diagnostics bundle: {ex.Message}");
        }
    }

    private static void AddTextEntry(
        ZipArchive archive,
        string entryName,
        string content)
    {
        var entry = archive.CreateEntry(
            entryName,
            CompressionLevel.Optimal);

        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // Every ZIP entry, including third-party logs, game history and INI,
        // must pass the same privacy boundary before diagnostic export.
        // Log sanitization alone cannot protect files we did not create.
        writer.Write(AppLogger.RedactSensitiveData(SanitizeUserPaths(content)));
    }

    private static string SanitizeUserPaths(string value)
    {
        foreach (var (path, token) in new[]
                 {
                     (
                         Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         "%USERPROFILE%"),
                     (
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "%LOCALAPPDATA%"),
                     (
                         Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "%APPDATA%")
                 }
                 .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
                 .OrderByDescending(item => item.Item1.Length))
        {
            value = value.Replace(
                path,
                token,
                StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Diagnostics cleanup is best-effort.
        }
    }

    private static List<string> DetectLoaderConflicts(
        string gameDir,
        string? managedProxy,
        bool reshadeManagedByOptiScaler)
    {
        var conflicts = new List<string>();
        foreach (var file in LoaderSignals)
        {
            var path = Path.Combine(gameDir, file);
            if (!File.Exists(path))
                continue;

            if (!string.IsNullOrWhiteSpace(managedProxy) &&
                file.Equals(managedProxy, StringComparison.OrdinalIgnoreCase))
                continue;

            if (file.Equals("ReShade64.dll", StringComparison.OrdinalIgnoreCase))
            {
                if (!reshadeManagedByOptiScaler)
                    conflicts.Add("ReShade64.dll");
            }
            else if (file.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase) ||
                     file.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase) ||
                     file.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase) ||
                     file.Equals("version.dll", StringComparison.OrdinalIgnoreCase) ||
                     file.Equals("winmm.dll", StringComparison.OrdinalIgnoreCase) ||
                     file.Equals("wininet.dll", StringComparison.OrdinalIgnoreCase) ||
                     file.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase) ||
                     file.Equals("dbghelp.dll", StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(file);
            }
        }

        return conflicts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ReadManifestStatus(string gameDir, string? exe)
    {
        var manifest = InstallerService.ReadManifest(gameDir);
        if (manifest == null)
            return null;

        if (exe == null)
            return "Game update tracking: executable not found";

        try
        {
            var current = HashService.Sha256(exe);
            return current.Equals(manifest.GameExecutableHash, StringComparison.OrdinalIgnoreCase)
                ? "Game update tracking: executable unchanged since install"
                : "Game update tracking: game executable changed since DLSS NR was installed";
        }
        catch
        {
            return "Game update tracking: unable to hash executable";
        }
    }

    private static string? FindMainExecutable(string gameDir)
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

    private static bool SafeFind(string root, string fileName)
    {
        if (!Directory.Exists(root))
            return false;

        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;

        var queue = new Queue<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((normalizedRoot, 0));

        while (queue.Count > 0 && visited.Count < 2000)
        {
            var (directory, depth) = queue.Dequeue();
            if (!visited.Add(directory))
                continue;

            if (File.Exists(Path.Combine(directory, fileName)))
                return true;

            if (depth >= 7)
                continue;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("_CommonRedist", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("__Installer", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    var fullChild = Path.GetFullPath(child);
                    if (!fullChild.StartsWith(
                            rootPrefix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    queue.Enqueue((fullChild, depth + 1));
                }
                catch
                {
                    // Ignore inaccessible or malformed child paths.
                }
            }
        }

        return false;
    }

    private static string[] ReadTail(string path, int maxLines)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            return File.ReadLines(path).TakeLast(maxLines).ToArray();
        }
        catch
        {
            return [];
        }
    }
}
