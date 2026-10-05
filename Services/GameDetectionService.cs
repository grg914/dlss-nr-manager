using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.Win32;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

public sealed class GameDetectionService
{
    private const int MaxConcurrentScans = 4;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    private static readonly string[] CompatibilitySignals =
    [
        "nvngx_dlss.dll",
        "nvngx_dlssd.dll",
        "sl.interposer.dll",
        "libxess.dll",
        "amd_fidelityfx_dx12.dll",
        "amd_fidelityfx_loader_dx12.dll"
    ];

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "game-scan-cache.json");

    public IReadOnlyList<DetectedGame> DetectCompatibleGames(bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            var cached = TryReadCache();
            if (cached != null)
                return cached;
        }

        var apps = DetectSteamApps()
            .Concat(DetectEpicApps())
            .Concat(DetectGogApps())
            .Concat(DetectUbisoftApps())
            .Concat(DetectEaApps())
            .Concat(DetectXboxApps())
            .Concat(DetectBattleNetApps())
            .Where(x => !string.IsNullOrWhiteSpace(x.Root) && Directory.Exists(x.Root))
            .GroupBy(x => x.Root, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var games = new ConcurrentBag<DetectedGame>();
        Parallel.ForEach(
            apps,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentScans },
            app =>
            {
                var detected = TryDetectGame(app.Name, app.Platform, app.Root);
                if (detected != null)
                    games.Add(detected);
            });

        var result = games
            .GroupBy(x => x.TargetDirectory, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => ConfidenceRank(x.Confidence)).First())
            .OrderByDescending(x => ConfidenceRank(x.Confidence))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TryWriteCache(result);
        return result;
    }

    public string? DetectCyberpunk()
        => DetectCompatibleGames()
            .FirstOrDefault(x => x.Name.Contains("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase))
            ?.TargetDirectory;

    public static string? Normalize(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;

        root = root.Trim().Trim('"');

        if (File.Exists(Path.Combine(root, "Cyberpunk2077.exe")))
            return root;

        var cyberpunk = Path.Combine(root, "bin", "x64");
        if (File.Exists(Path.Combine(cyberpunk, "Cyberpunk2077.exe")))
            return cyberpunk;

        if (Directory.Exists(root) && SafeEnumerateFiles(root, "*.exe").Any())
            return root;

        return FindBestTargetDirectory(root);
    }

    private static DetectedGame? TryDetectGame(string name, string platform, string root)
    {
        var known = DetectKnownGame(name, platform, root);
        if (known != null)
            return known;

        var target = FindBestTargetDirectory(root);
        if (target == null)
            return null;

        var evidence = GetSignals(target);
        if (evidence.Count == 0)
            evidence = FindSignalsSafe(root, 6)
                .Select(Path.GetFileName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (evidence.Count == 0)
            return null;

        var hasDlss = evidence.Any(x => x.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase));
        var hasStreamline = evidence.Any(x => x.Equals("sl.interposer.dll", StringComparison.OrdinalIgnoreCase));
        var hasOtherTemporal = evidence.Any(x =>
            x.Equals("libxess.dll", StringComparison.OrdinalIgnoreCase) ||
            x.StartsWith("amd_fidelityfx", StringComparison.OrdinalIgnoreCase));

        var confidence = hasDlss && hasStreamline
            ? "Probable"
            : hasDlss || hasOtherTemporal
                ? "Candidate"
                : "Candidate";

        return new DetectedGame(
            name,
            platform,
            root,
            target,
            confidence,
            string.Join(", ", evidence),
            "dxgi.dll");
    }

    private static DetectedGame? DetectKnownGame(string name, string platform, string root)
    {
        if (name.Contains("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase))
        {
            var target = Path.Combine(root, "bin", "x64");
            if (File.Exists(Path.Combine(target, "Cyberpunk2077.exe")))
                return new(name, platform, root, target, "Validated", "Upstream validated path: bin\\x64", "dbghelp.dll");
        }

        if (name.Contains("Baldur", StringComparison.OrdinalIgnoreCase) &&
            name.Contains("Gate 3", StringComparison.OrdinalIgnoreCase))
        {
            var target = Path.Combine(root, "bin");
            if (File.Exists(Path.Combine(target, "bg3.exe")) ||
                File.Exists(Path.Combine(target, "bg3_dx11.exe")))
                return new(name, platform, root, target, "Validated", "Upstream validated path: bin", "dxgi.dll");
        }

        if (name.Contains("Hogwarts Legacy", StringComparison.OrdinalIgnoreCase))
        {
            var target = Path.Combine(root, "Phoenix", "Binaries", "Win64");
            if (Directory.Exists(target) && SafeEnumerateFiles(target, "*.exe").Any())
                return new(name, platform, root, target, "Validated", "Upstream validated path: Phoenix\\Binaries\\Win64", "dxgi.dll");
        }

        return null;
    }

    private static string? FindBestTargetDirectory(string root)
    {
        if (!Directory.Exists(root))
            return null;

        string? firstGameExe = null;
        var queue = new Queue<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((root, 0));

        while (queue.Count > 0 && visited.Count < 2500)
        {
            var (dir, depth) = queue.Dequeue();
            if (!visited.Add(dir))
                continue;

            var exes = SafeEnumerateFiles(dir, "*.exe")
                .Where(x => !IsLikelyLauncherOrInstaller(Path.GetFileName(x)))
                .ToList();

            firstGameExe ??= exes.FirstOrDefault();

            if (exes.Count > 0 && CompatibilitySignals.Any(s => File.Exists(Path.Combine(dir, s))))
                return dir;

            if (depth >= 7)
                continue;

            foreach (var child in SafeEnumerateDirectories(dir))
            {
                var name = Path.GetFileName(child);
                if (ShouldSkipDirectory(name))
                    continue;

                queue.Enqueue((child, depth + 1));
            }
        }

        if (firstGameExe != null)
            return Path.GetDirectoryName(firstGameExe);

        return null;
    }

    private static List<string> GetSignals(string directory)
        => CompatibilitySignals
            .Where(signal => File.Exists(Path.Combine(directory, signal)))
            .ToList();

    private static List<string> FindSignalsSafe(string root, int maxResults)
    {
        var result = new List<string>();
        if (!Directory.Exists(root))
            return result;

        var queue = new Queue<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((root, 0));

        while (queue.Count > 0 && result.Count < maxResults && visited.Count < 2500)
        {
            var (dir, depth) = queue.Dequeue();
            if (!visited.Add(dir))
                continue;

            foreach (var signal in CompatibilitySignals)
            {
                var path = Path.Combine(dir, signal);
                if (File.Exists(path))
                {
                    result.Add(path);
                    if (result.Count >= maxResults)
                        return result;
                }
            }

            if (depth >= 7)
                continue;

            foreach (var child in SafeEnumerateDirectories(dir))
            {
                if (!ShouldSkipDirectory(Path.GetFileName(child)))
                    queue.Enqueue((child, depth + 1));
            }
        }

        return result;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool ShouldSkipDirectory(string name)
        => name.Equals(".git", StringComparison.OrdinalIgnoreCase)
           || name.Equals("__Installer", StringComparison.OrdinalIgnoreCase)
           || name.Equals("redist", StringComparison.OrdinalIgnoreCase)
           || name.Equals("_CommonRedist", StringComparison.OrdinalIgnoreCase)
           || name.Contains("crash", StringComparison.OrdinalIgnoreCase);

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

    private static IReadOnlyList<DetectedGame>? TryReadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return null;

            var cache = JsonSerializer.Deserialize<ScanCache>(File.ReadAllText(CachePath));
            if (cache == null || DateTimeOffset.UtcNow - cache.CreatedAt > CacheLifetime)
                return null;

            var existing = cache.Games.Where(x => Directory.Exists(x.TargetDirectory)).ToList();
            return existing.Count == 0 ? null : existing;
        }
        catch
        {
            return null;
        }
    }

    private static void TryWriteCache(IReadOnlyList<DetectedGame> games)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(
                CachePath,
                JsonSerializer.Serialize(new ScanCache(DateTimeOffset.UtcNow, games.ToList()),
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private sealed record ScanCache(DateTimeOffset CreatedAt, List<DetectedGame> Games);

    private static IEnumerable<(string Name, string Platform, string Root)> DetectSteamApps()
    {
        foreach (var library in GetSteamLibraries())
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
                continue;

            foreach (var manifest in SafeEnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                string? name = null;
                string? installDir = null;
                try
                {
                    foreach (var line in File.ReadLines(manifest))
                    {
                        var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (parts.Length < 2)
                            continue;

                        if (parts[0].Equals("name", StringComparison.OrdinalIgnoreCase))
                            name = parts[1];
                        else if (parts[0].Equals("installdir", StringComparison.OrdinalIgnoreCase))
                            installDir = parts[1];
                    }
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(installDir))
                    yield return (name, "Steam", Path.Combine(steamApps, "common", installDir));
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
            if (!File.Exists(vdf))
                continue;

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

    private static IEnumerable<(string Name, string Platform, string Root)> DetectEpicApps()
    {
        var manifestDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestDir))
            yield break;

        foreach (var file in SafeEnumerateFiles(manifestDir, "*.item"))
        {
            JsonDocument? json = null;
            try
            {
                json = JsonDocument.Parse(File.ReadAllText(file));
                var root = json.RootElement;
                if (!root.TryGetProperty("InstallLocation", out var installLocation))
                    continue;

                var path = installLocation.GetString();
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var name = root.TryGetProperty("DisplayName", out var displayName) &&
                           !string.IsNullOrWhiteSpace(displayName.GetString())
                    ? displayName.GetString()!
                    : Path.GetFileName(path);

                yield return (name, "Epic", path);
            }
            finally
            {
                json?.Dispose();
            }
        }
    }

    private static IEnumerable<(string Name, string Platform, string Root)> DetectGogApps()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = SafeOpenBaseKey(hive, view);
            using var games = baseKey?.OpenSubKey(@"SOFTWARE\GOG.com\Games")
                             ?? baseKey?.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            if (games == null)
                continue;

            foreach (var subName in games.GetSubKeyNames())
            {
                using var game = games.OpenSubKey(subName);
                var path = game?.GetValue("path") as string;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var name = game?.GetValue("gameName") as string
                           ?? game?.GetValue("name") as string
                           ?? $"GOG {subName}";

                yield return (name, "GOG", path);
            }
        }
    }

    private static IEnumerable<(string Name, string Platform, string Root)> DetectUbisoftApps()
    {
        using var hklm = SafeOpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var installs = hklm?.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
        if (installs == null)
            yield break;

        foreach (var id in installs.GetSubKeyNames())
        {
            using var item = installs.OpenSubKey(id);
            var path = item?.GetValue("InstallDir") as string;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                continue;

            yield return (Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), "Ubisoft Connect", path);
        }
    }

    private static IEnumerable<(string Name, string Platform, string Root)> DetectEaApps()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var baseKey = SafeOpenBaseKey(hive, view);
            using var uninstall = baseKey?.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null)
                continue;

            foreach (var subName in uninstall.GetSubKeyNames())
            {
                using var item = uninstall.OpenSubKey(subName);
                var uninstallString = item?.GetValue("UninstallString")?.ToString() ?? string.Empty;
                if (!uninstallString.Contains("EAInstaller", StringComparison.OrdinalIgnoreCase) ||
                    !uninstallString.Contains("Cleanup.exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                var path = item?.GetValue("InstallLocation")?.ToString();
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    continue;

                var name = item?.GetValue("DisplayName")?.ToString() ?? Path.GetFileName(path);
                yield return (name, "EA App", path);
            }
        }
    }

    private static IEnumerable<(string Name, string Platform, string Root)> DetectXboxApps()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType is DriveType.Network or DriveType.CDRom)
                continue;

            var gamingRoot = Path.Combine(drive.RootDirectory.FullName, ".GamingRoot");
            if (!File.Exists(gamingRoot))
                continue;

            string? libraryRoot = null;
            try
            {
                var bytes = File.ReadAllBytes(gamingRoot);
                if (bytes.Length > 5 && bytes[0] == 'R' && bytes[1] == 'G' && bytes[2] == 'B' && bytes[3] == 'X')
                {
                    var sb = new StringBuilder(drive.RootDirectory.FullName);
                    for (var i = 5; i < bytes.Length; i++)
                        if (bytes[i] != 0)
                            sb.Append((char)bytes[i]);
                    libraryRoot = sb.ToString();
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
                continue;

            foreach (var gameDirectory in SafeEnumerateDirectories(libraryRoot))
            {
                var content = Path.Combine(gameDirectory, "Content");
                var config = Path.Combine(content, "MicrosoftGame.config");
                if (!File.Exists(config))
                    continue;

                var name = Path.GetFileName(gameDirectory);
                try
                {
                    var xml = new XmlDocument();
                    xml.Load(config);
                    var identityName = xml.DocumentElement?
                        .SelectSingleNode("/Game/Identity")?
                        .Attributes?["Name"]?.Value;
                    if (!string.IsNullOrWhiteSpace(identityName))
                        name = identityName;
                }
                catch { }

                yield return (name, "Xbox App", content);
            }
        }
    }

    private static IEnumerable<(string Name, string Platform, string Root)> DetectBattleNetApps()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var baseKey = SafeOpenBaseKey(hive, view);
            using var uninstall = baseKey?.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null)
                continue;

            foreach (var subName in uninstall.GetSubKeyNames())
            {
                using var item = uninstall.OpenSubKey(subName);
                var publisher = item?.GetValue("Publisher")?.ToString() ?? string.Empty;
                var name = item?.GetValue("DisplayName")?.ToString() ?? string.Empty;
                if (!publisher.Contains("Blizzard", StringComparison.OrdinalIgnoreCase) &&
                    !publisher.Contains("Activision", StringComparison.OrdinalIgnoreCase))
                    continue;

                var path = item?.GetValue("InstallLocation")?.ToString();
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    continue;

                yield return (string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name, "Battle.net", path);
            }
        }
    }

    private static RegistryKey? SafeOpenBaseKey(RegistryHive hive, RegistryView view)
    {
        try { return RegistryKey.OpenBaseKey(hive, view); }
        catch { return null; }
    }
}
