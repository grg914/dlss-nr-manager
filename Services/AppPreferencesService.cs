using System.Globalization;
using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record AppPreferences(
    bool SoftwareRendering,
    string Language = "");

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
                return new(false, GetDefaultLanguage());

            var loaded =
                JsonSerializer.Deserialize<AppPreferences>(
                    File.ReadAllText(FilePath))
                ?? new(false, GetDefaultLanguage());

            return loaded with
            {
                Language = UiLocalizationService.NormalizeLanguage(
                    string.IsNullOrWhiteSpace(loaded.Language)
                        ? GetDefaultLanguage()
                        : loaded.Language)
            };
        }
        catch
        {
            return new(false, GetDefaultLanguage());
        }
    }

    public static void Save(AppPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        var normalized = preferences with
        {
            Language = UiLocalizationService.NormalizeLanguage(
                preferences.Language)
        };

        AtomicFile.WriteAllText(
            FilePath,
            JsonSerializer.Serialize(
                normalized,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GetDefaultLanguage()
        => CultureInfo.CurrentUICulture
               .TwoLetterISOLanguageName
               .Equals(
                   "fr",
                   StringComparison.OrdinalIgnoreCase)
            ? "fr"
            : "en";
}
