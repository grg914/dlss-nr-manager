using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record ScanSettings(
    IReadOnlyList<string> CustomRoots,
    bool ScanAllFixedDrives);

public static class ScanSettingsService
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "scan-settings.json");

    public static ScanSettings Load()
    {
        try
        {
            if (!File.Exists(PathName))
                return new([], false);

            var settings = JsonSerializer.Deserialize<ScanSettings>(
                File.ReadAllText(PathName));

            return settings ?? new([], false);
        }
        catch
        {
            return new([], false);
        }
    }

    public static void Save(ScanSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);

        var normalized = settings.CustomRoots
            .Where(Directory.Exists)
            .Select(System.IO.Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AtomicFile.WriteAllText(
            PathName,
            JsonSerializer.Serialize(
                new ScanSettings(normalized, settings.ScanAllFixedDrives),
                new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void AddRoot(string root)
    {
        var settings = Load();
        var roots = settings.CustomRoots
            .Append(System.IO.Path.GetFullPath(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Save(settings with { CustomRoots = roots });
    }

    public static void RemoveRoot(string root)
    {
        var settings = Load();
        var roots = settings.CustomRoots
            .Where(x => !x.Equals(root, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Save(settings with { CustomRoots = roots });
    }
}
