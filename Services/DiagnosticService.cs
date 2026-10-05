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
        lines.Add($"DLSSNR runtime: {(state.RuntimePresent ? (state.RuntimeHashValid ? "valid hash" : "hash mismatch") : "missing")}");

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

        var conflicts = DetectLoaderConflicts(gameDir, state.ProxyName);
        lines.Add(conflicts.Count == 0
            ? "Loader conflicts: none detected"
            : $"Potential loader conflicts: {string.Join(", ", conflicts)}");

        var manifestStatus = ReadManifestStatus(gameDir, exe);
        if (manifestStatus != null)
            lines.Add(manifestStatus);

        var ready = state.Installed &&
                    state.RuntimePresent &&
                    state.RuntimeHashValid &&
                    temporal.Count > 0 &&
                    exe != null &&
                    conflicts.Count == 0;

        var summary = nrRunning
            ? "DLSS NR is confirmed running."
            : ready
                ? "Installation looks ready. Launch the game and verify DLSS NR reports Running."
                : "Configuration needs attention before DLSS NR can be considered ready.";

        return new(summary, lines, ready, loaded, nrRunning);
    }

    private static List<string> DetectLoaderConflicts(string gameDir, string? managedProxy)
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
                conflicts.Add("ReShade64.dll");
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
            var current = HashService.Sha256Async(exe).GetAwaiter().GetResult();
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
        try
        {
            if (File.Exists(Path.Combine(root, fileName)))
                return true;

            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).Take(1).Any();
        }
        catch
        {
            return false;
        }
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
