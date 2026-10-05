using Microsoft.Win32;
namespace DlssNrManager.Services;
public sealed class GameDetectionService
{
    public string? DetectCyberpunk()
    {
        foreach (var candidate in DetectSteam().Concat(DetectGog()).Concat(DetectEpic()))
        {
            var x64 = Normalize(candidate);
            if (x64 != null) return x64;
        }
        return null;
    }

    private static IEnumerable<string> DetectSteam()
    {
        var roots = new List<string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive == Registry.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(hive == Registry.CurrentUser ? @"Software\Valve\Steam" : @"SOFTWARE\WOW6432Node\Valve\Steam");
            var root = key?.GetValue("SteamPath") as string ?? key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(root)) roots.Add(root);
        } catch { }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(root, "steamapps", "common", "Cyberpunk 2077");
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (var line in File.ReadLines(vdf))
            {
                var q = line.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (q.Length >= 2 && q[0].All(char.IsDigit))
                    yield return Path.Combine(q[1].Replace(@"\\", @"\"), "steamapps", "common", "Cyberpunk 2077");
            }
        }
    }

    private static IEnumerable<string> DetectGog()
    {
        foreach (var path in new[] {
            @"SOFTWARE\WOW6432Node\GOG.com\Games\1423049311",
            @"SOFTWARE\GOG.com\Games\1423049311" })
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key?.GetValue("path") is string p) yield return p;
            } catch { }
        }
    }

    private static IEnumerable<string> DetectEpic()
    {
        var manifestDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifestDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(manifestDir, "*.item"))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase)) continue;
            var marker = "\"InstallLocation\":";
            var i = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var start = text.IndexOf('"', i + marker.Length);
            var end = start < 0 ? -1 : text.IndexOf('"', start + 1);
            if (start >= 0 && end > start) yield return text[(start + 1)..end].Replace(@"\\", @"\");
        }
    }

    public static string? Normalize(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        root = root.Trim().Trim('"');
        var direct = Path.Combine(root, "Cyberpunk2077.exe");
        if (File.Exists(direct)) return root;
        var x64 = Path.Combine(root, "bin", "x64");
        return File.Exists(Path.Combine(x64, "Cyberpunk2077.exe")) ? x64 : null;
    }
}