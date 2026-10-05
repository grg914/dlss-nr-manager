using System.Text.Json;
using Microsoft.Win32;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

public sealed class GameDetectionService
{
    private static readonly string[] CompatibilitySignals =
    [
        "nvngx_dlss.dll",
        "nvngx_dlssd.dll",
        "sl.interposer.dll",
        "libxess.dll",
        "amd_fidelityfx_dx12.dll",
        "amd_fidelityfx_loader_dx12.dll"
    ];

    public IReadOnlyList<DetectedGame> DetectCompatibleGames()
    {
        var games = new List<DetectedGame>();

        foreach (var app in DetectSteamApps())
            TryAddGame(games, app.Name, "Steam", app.Root);

        foreach (var app in DetectEpicApps())
            TryAddGame(games, app.Name, "Epic", app.Root);

        foreach (var app in DetectGogApps())
            TryAddGame(games, app.Name, "GOG", app.Root);

        return games
            .GroupBy(x => x.TargetDirectory, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => ConfidenceRank(x.Confidence)).First())
            .OrderByDescending(x => ConfidenceRank(x.Confidence))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? DetectCyberpunk()
        => DetectCompatibleGames()
            .FirstOrDefault(x => x.Name.Contains("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase))
            ?.TargetDirectory;

    public static string? Normalize(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        root = root.Trim().Trim('"');

        if (File.Exists(Path.Combine(root, "Cyberpunk2077.exe")))
            return root;

        var cyberpunk = Path.Combine(root, "bin", "x64");
        if (File.Exists(Path.Combine(cyberpunk, "Cyberpunk2077.exe")))
            return cyberpunk;

        if (Directory.Exists(root) && Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly).Any())
            return root;

        return FindBestTargetDirectory(root);
    }

    private static void TryAddGame(List<DetectedGame> games, string name, string platform, string root)
    {
        if (!Directory.Exists(root)) return;

        var known = DetectKnownGame(name, platform, root);
        if (known != null)
        {
            games.Add(known);
            return;
        }

        var target = FindBestTargetDirectory(root);
        if (target == null) return;

        var evidence = GetSignals(target);
        if (evidence.Count == 0)
        {
            var rootSignals = FindSignalsRecursive(root, maxResults: 3);
            if (rootSignals.Count == 0) return;
            evidence = rootSignals.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var hasDlss = evidence.Any(x => x.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase));
        var hasStreamline = evidence.Any(x => x.Equals("sl.interposer.dll", StringComparison.OrdinalIgnoreCase));
        var hasOtherTemporal = evidence.Any(x =>
            x.Equals("libxess.dll", StringComparison.OrdinalIgnoreCase) ||
            x.StartsWith("amd_fidelityfx", StringComparison.OrdinalIgnoreCase));

        var confidence = hasDlss && hasStreamline ? "Probable" :
                         hasDlss || hasOtherTemporal ? "Candidate" : "Candidate";

        games.Add(new DetectedGame(
            name,
            platform,
            root,
            target,
            confidence,
            string.Join(", ", evidence),
            "dxgi.dll"));
    }

    private static DetectedGame? DetectKnownGame(string name, string platform, string root)
    {
        if (name.Contains("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase))
        {
            var target = Path.Combine(root, "bin", "x64");
            if (File.Exists(Path.Combine(target, "Cyberpunk2077.exe")))
                return new(name, platform, root, target, "Validated", "Upstream validated path: bin\\x64", "dbghelp.dll");
        }

        if (name.Contains("Baldur", StringComparison.OrdinalIgnoreCase) && name.Contains("Gate 3", StringComparison.OrdinalIgnoreCase))
        {
            var target = Path.Combine(root, "bin");
            if (File.Exists(Path.Combine(target, "bg3.exe")) || File.Exists(Path.Combine(target, "bg3_dx11.exe")))
                return new(name, platform, root, target, "Validated", "Upstream validated path: bin", "dxgi.dll");
        }

        if (name.Contains("Hogwarts Legacy", StringComparison.OrdinalIgnoreCase))
        {
            var target = Path.Combine(root, "Phoenix", "Binaries", "Win64");
            if (Directory.Exists(target) && Directory.EnumerateFiles(target, "*.exe", SearchOption.TopDirectoryOnly).Any())
                return new(name, platform, root, target, "Validated", "Upstream validated path: Phoenix\\Binaries\\Win64", "dxgi.dll");
        }

        return null;
    }

    private static string? FindBestTargetDirectory(string root)
    {
        try
        {
            var signalFiles = FindSignalsRecursive(root, maxResults: 12);
            foreach (var signal in signalFiles)
            {
                var dir = Path.GetDirectoryName(signal);
                if (dir != null && Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly).Any())
                    return dir;
            }

            var exe = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault(x => !IsLikelyLauncherOrInstaller(Path.GetFileName(x)));

            return exe == null ? null : Path.GetDirectoryName(exe);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GetSignals(string directory)
    {
        var result = new List<string>();
        foreach (var signal in CompatibilitySignals)
        {
            if (File.Exists(Path.Combine(directory, signal)))
                result.Add(signal);
        }
        return result;
    }

    private static List<string> FindSignalsRecursive(string root, int maxResults)
    {
        var result = new List<string>();
        try
        {
            foreach (var signal in CompatibilitySignals)
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(root, signal, SearchOption.AllDirectories); }
                catch { continue; }

                foreach (var file in files)
                {
                    result.Add(file);
                    if (result.Count >= maxResults)
                        return result;
                }
            }
        }
        catch { }
        return result;
    }

    private static bool IsLikelyLauncherOrInstaller(string fileName)
        => fileName.Contains("launcher", StringComparison.OrdinalIgnoreCase)
           || fileName.Contains("unins", StringComparison.OrdinalIgnoreCase)
           || fileName.Contains("setup", StringComparison.OrdinalIgnoreCase)
           || fileName.Contains("crash", StringComparison.OrdinalIgnoreCase)
           || fileName.Contains("report", StringComparison.OrdinalIgnoreCase);

    private static int ConfidenceRank(string confidence)
        => confidence switch
        {
            "Validated" => 3,
            "Probable" => 2,
            _ => 1
        };

    private static IEnumerable<(string Name, string Root)> DetectSteamApps()
    {
        foreach (var library in GetSteamLibraries())
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps)) continue;

            foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                string? name = null;
                string? installDir = null;

                try
                {
                    foreach (var line in File.ReadLines(manifest))
                    {
                        var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (parts.Length < 2) continue;

                        if (parts[0].Equals("name", StringComparison.OrdinalIgnoreCase))
                            name = parts[1];
                        else if (parts[0].Equals("installdir", StringComparison.OrdinalIgnoreCase))
                            installDir = parts[1];
                    }
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(installDir))
                    yield return (name, Path.Combine(steamApps, "common", installDir));
            }
        }
    }

    private static IEnumerable<string> GetSteamLibraries()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var keyPath in new[] { @"Software\Valve\Steam", @"SOFTWARE\WOW6432Node\Valve\Steam" })
                {
                    using var key = baseKey.OpenSubKey(keyPath);
                    var root = key?.GetValue("SteamPath") as string ?? key?.GetValue("InstallPath") as string;
                    if (!string.IsNullOrWhiteSpace(root))
                        roots.Add(root);
                }
            }
            catch { }
        }

        foreach (var root in roots.ToList())
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            try
            {
                foreach (var line in File.ReadLines(vdf))
                {
                    var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length >= 2 && parts[0].Equals("path", StringComparison.OrdinalIgnoreCase))
                        roots.Add(parts[1].Replace(@"\\", @"\"));
                    else if (parts.Length >= 2 && parts[0].All(char.IsDigit) && Path.IsPathRooted(parts[1]))
                        roots.Add(parts[1].Replace(@"\\", @"\"));
                }
            }
            catch { }
        }

        return roots;
    }

    private static IEnumerable<(string Name, string Root)> DetectEpicApps()
    {
        var manifestDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestDir)) yield break;

        foreach (var file in Directory.EnumerateFiles(manifestDir, "*.item"))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                var root = json.RootElement;

                if (!root.TryGetProperty("InstallLocation", out var installLocation)) continue;
                var path = installLocation.GetString();
                if (string.IsNullOrWhiteSpace(path)) continue;

                string name = "Epic game";
                if (root.TryGetProperty("DisplayName", out var displayName) && !string.IsNullOrWhiteSpace(displayName.GetString()))
                    name = displayName.GetString()!;

                yield return (name, path);
            }
            catch { }
        }
    }

    private static IEnumerable<(string Name, string Root)> DetectGogApps()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            RegistryKey? games = null;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                games = baseKey.OpenSubKey(@"SOFTWARE\GOG.com\Games")
                        ?? baseKey.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");

                if (games == null) continue;

                foreach (var subName in games.GetSubKeyNames())
                {
                    using var game = games.OpenSubKey(subName);
                    var path = game?.GetValue("path") as string;
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    var name = game?.GetValue("gameName") as string
                               ?? game?.GetValue("name") as string
                               ?? $"GOG {subName}";

                    yield return (name, path);
                }
            }
            finally
            {
                games?.Dispose();
            }
        }
    }
}
