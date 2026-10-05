namespace DlssNrManager.Services;
public static class IniService
{
    public static void ApplyCyberpunkPreset(string iniPath)
    {
        if (!File.Exists(iniPath)) return;
        var lines = File.ReadAllLines(iniPath).ToList();

        var dlssNrSection = FindSection(lines, "DlssNr");
        if (dlssNrSection >= 0)
        {
            SetIfPresent(lines, dlssNrSection, "Enabled", "true");
            SetIfPresent(lines, dlssNrSection, "RunBeforeSR", "true");
            SetIfPresent(lines, dlssNrSection, "Passes", "1");
            SetIfPresent(lines, dlssNrSection, "WorkingScale", "1.0");
            SetIfPresent(lines, dlssNrSection, "Style", "1");
        }

        var menuSection = FindSection(lines, "Menu");
        if (menuSection >= 0)
        {
            SetIfPresent(lines, menuSection, "OverlayMenu", "true");
            SetIfPresent(lines, menuSection, "ShortcutKey", "0x79"); // VK_F10
            SetIfPresent(lines, menuSection, "ShowFps", "true");
            SetIfPresent(lines, menuSection, "FpsOverlayType", "1"); // Simple FPS overlay
        }

        File.WriteAllLines(iniPath, lines);
    }

    private static int FindSection(List<string> lines, string name)
        => lines.FindIndex(x => x.Trim().Equals($"[{name}]", StringComparison.OrdinalIgnoreCase));

    private static void SetIfPresent(List<string> lines, int section, string key, string value)
    {
        for (var i = section + 1; i < lines.Count && !lines[i].TrimStart().StartsWith("["); i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                var indent = lines[i][..(lines[i].Length - t.Length)];
                lines[i] = $"{indent}{key}={value}";
                return;
            }
        }
    }
}