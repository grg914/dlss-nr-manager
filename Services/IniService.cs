namespace DlssNrManager.Services;

public static class IniService
{
    public static void ApplyPreset(string iniPath, string workingScale, bool enableNeuralRendering = true)
    {
        if (!File.Exists(iniPath))
            return;

        var lines = File.ReadAllLines(iniPath).ToList();

        var dlssNrSection = FindSection(lines, "DlssNr");
        if (dlssNrSection >= 0)
        {
            SetOrAdd(lines, dlssNrSection, "Enabled", enableNeuralRendering ? "true" : "false");
            SetOrAdd(lines, dlssNrSection, "RunBeforeSR", "true");
            SetOrAdd(lines, dlssNrSection, "Passes", "1");
            SetOrAdd(lines, dlssNrSection, "WorkingScale", workingScale);
            SetOrAdd(lines, dlssNrSection, "Style", "1");
        }
        else if (enableNeuralRendering)
        {
            throw new InvalidDataException(
                "OptiScaler.ini does not contain the [DlssNr] section required for Neural Rendering. " +
                "The selected OptiScaler package is not compatible with this preset.");
        }

        var menuSection = FindSection(lines, "Menu");
        if (menuSection >= 0)
        {
            SetOrAdd(lines, menuSection, "OverlayMenu", "true");
            SetOrAdd(lines, menuSection, "ShortcutKey", "0x79"); // VK_F10
            SetOrAdd(lines, menuSection, "ShowFps", "true");
            SetOrAdd(lines, menuSection, "FpsOverlayType", "1");
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
            SetOrAdd(lines, menuSection, "ShowFps", showFps ? "true" : "false");
            SetOrAdd(lines, menuSection, "FpsOverlayType", Math.Clamp(fpsType, 0, 6).ToString());
            SetOrAdd(lines, menuSection, "FpsOverlayPos", Math.Clamp(fpsPosition, 0, 3).ToString());
            SetOrAdd(lines, menuSection, "OverlayMenu", "true");
            SetOrAdd(lines, menuSection, "ShortcutKey", "0x79");
        }

        var processSection = FindSection(lines, "ProcessFilter");
        if (processSection >= 0)
        {
            SetOrAdd(
                lines,
                processSection,
                "TargetProcessName",
                string.IsNullOrWhiteSpace(targetProcessName) ? "auto" : targetProcessName.Trim());
        }

        var pluginsSection = FindSection(lines, "Plugins");
        if (pluginsSection >= 0)
            SetOrAdd(lines, pluginsSection, "LoadReshade", loadReShade ? "true" : "false");

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
            if (value.Length == 0 || value.StartsWith(';') || value.StartsWith('#'))
                continue;

            var separator = value.IndexOf('=');
            if (separator <= 0)
                continue;

            var candidateKey = value[..separator].Trim();
            if (!candidateKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return value[(separator + 1)..].Trim();
        }

        return null;
    }

    private static int FindSection(List<string> lines, string name)
        => lines.FindIndex(x => x.Trim().Equals($"[{name}]", StringComparison.OrdinalIgnoreCase));

    private static void SetOrAdd(List<string> lines, int section, string key, string value)
    {
        var insertAt = lines.Count;

        for (var i = section + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("["))
            {
                insertAt = i;
                break;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0 ||
                !trimmed[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            var indent = lines[i][..(lines[i].Length - trimmed.Length)];
            lines[i] = $"{indent}{key}={value}";
            return;
        }

        lines.Insert(insertAt, $"{key}={value}");
    }
}
