using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record AppPreferences(bool SoftwareRendering);

public static class AppPreferencesService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "preferences.json");

    public static AppPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new(false);

            return JsonSerializer.Deserialize<AppPreferences>(
                       File.ReadAllText(FilePath))
                   ?? new(false);
        }
        catch
        {
            return new(false);
        }
    }

    public static void Save(AppPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        AtomicFile.WriteAllText(
            FilePath,
            JsonSerializer.Serialize(
                preferences,
                new JsonSerializerOptions { WriteIndented = true }));
    }
}
