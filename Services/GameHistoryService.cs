using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record GameHistoryEntry(
    DateTimeOffset At,
    string Action,
    string Summary,
    string? Backup = null);

public static class GameHistoryService
{
    private const string FileName = ".dlssnr-manager-history.json";
    private const int MaxEntries = 200;

    public static IReadOnlyList<GameHistoryEntry> Read(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, FileName);
            if (!File.Exists(path))
                return [];

            return JsonSerializer.Deserialize<List<GameHistoryEntry>>(
                       File.ReadAllText(path))
                   ?.OrderByDescending(x => x.At)
                   .ToList()
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Append(
        string gameDir,
        string action,
        string summary,
        string? backup = null)
    {
        var entries = Read(gameDir)
            .OrderBy(x => x.At)
            .ToList();

        entries.Add(new GameHistoryEntry(
            DateTimeOffset.UtcNow,
            action,
            summary,
            backup));

        if (entries.Count > MaxEntries)
            entries = entries[^MaxEntries..];

        AtomicFile.WriteAllText(
            Path.Combine(gameDir, FileName),
            JsonSerializer.Serialize(
                entries,
                new JsonSerializerOptions { WriteIndented = true }));
    }
}
