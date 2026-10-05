namespace DlssNrManager.Services;
public static class IniService
{
    public static void ApplyCyberpunkPreset(string iniPath)
    {
        if (!File.Exists(iniPath)) return;
        var lines = File.ReadAllLines(iniPath).ToList();
        var section = FindSection(lines, "DlssNr");
        if (section < 0) return;
        SetIfPresent(lines, section, "Enabled", "true");
        SetIfPresent(lines, section, "RunBeforeSR", "true");
        SetIfPresent(lines, section, "Passes", "1");
        SetIfPresent(lines, section, "WorkingScale", "1.0");
        SetIfPresent(lines, section, "Style", "1");
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