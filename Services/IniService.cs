namespace DlssNrManager.Services;

public static class IniService
{
    public static void ApplyPreset(string iniPath, string workingScale)
    {
        if (!File.Exists(iniPath))
            return;

        var lines = File.ReadAllLines(iniPath).ToList();

        var dlssNrSection = FindSection(lines, "DlssNr");
        if (dlssNrSection >= 0)
        {
            SetIfPresent(lines, dlssNrSection, "Enabled", "true");
            SetIfPresent(lines, dlssNrSection, "RunBeforeSR", "true");
            SetIfPresent(lines, dlssNrSection, "Passes", "1");
            SetIfPresent(lines, dlssNrSection, "WorkingScale", workingScale);
            SetIfPresent(lines, dlssNrSection, "Style", "1");
        }

        var menuSection = FindSection(lines, "Menu");
        if (menuSection >= 0)
        {
            SetIfPresent(lines, menuSection, "OverlayMenu", "true");
            SetIfPresent(lines, menuSection, "ShortcutKey", "0x79"); // VK_F10
            SetIfPresent(lines, menuSection, "ShowFps", "true");
            SetIfPresent(lines, menuSection, "FpsOverlayType", "1");
        }

        File.WriteAllLines(iniPath, lines);
    }

    public static void ApplyAdvancedSettings(
        string iniPath,
        int fpsType,
        int fpsPosition,
        bool showFps,
        string? targetProcessName,
        bool loadReShade)
    {
        if (!File.Exists(iniPath))
            return;

        var lines = File.ReadAllLines(iniPath).ToList();

        var menuSection = FindSection(lines, "Menu");
        if (menuSection >= 0)
        {
            SetIfPresent(lines, menuSection, "ShowFps", showFps ? "true" : "false");
            SetIfPresent(lines, menuSection, "FpsOverlayType", Math.Clamp(fpsType, 0, 6).ToString());
            SetIfPresent(lines, menuSection, "FpsOverlayPos", Math.Clamp(fpsPosition, 0, 3).ToString());
            SetIfPresent(lines, menuSection, "OverlayMenu", "true");
            SetIfPresent(lines, menuSection, "ShortcutKey", "0x79");
        }

        var processSection = FindSection(lines, "ProcessFilter");
        if (processSection >= 0)
        {
            SetIfPresent(
                lines,
                processSection,
                "TargetProcessName",
                string.IsNullOrWhiteSpace(targetProcessName) ? "auto" : targetProcessName.Trim());
        }

        var pluginsSection = FindSection(lines, "Plugins");
        if (pluginsSection >= 0)
            SetIfPresent(lines, pluginsSection, "LoadReshade", loadReShade ? "true" : "false");

        File.WriteAllLines(iniPath, lines);
    }

    public static string? ReadValue(string iniPath, string section, string key)
    {
        if (!File.Exists(iniPath))
            return null;

        var lines = File.ReadAllLines(iniPath).ToList();
        var sectionIndex = FindSection(lines, section);
        if (sectionIndex < 0)
            return null;

        for (var i = sectionIndex + 1; i < lines.Count && !lines[i].TrimStart().StartsWith("["); i++)
        {
            var value = lines[i].Trim();
            if (!value.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                continue;

            return value[(value.IndexOf('=') + 1)..].Trim();
        }

        return null;
    }

    private static int FindSection(List<string> lines, string name)
        => lines.FindIndex(x => x.Trim().Equals($"[{name}]", StringComparison.OrdinalIgnoreCase));

    private static void SetIfPresent(List<string> lines, int section, string key, string value)
    {
        for (var i = section + 1; i < lines.Count && !lines[i].TrimStart().StartsWith("["); i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                continue;

            var indent = lines[i][..(lines[i].Length - trimmed.Length)];
            lines[i] = $"{indent}{key}={value}";
            return;
        }
    }
}
