using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record FileTransactionState(
    string Id,
    string Operation,
    string BackupDirectory,
    DateTimeOffset StartedAt,
    string Stage,
    IReadOnlyList<string> Files);

public sealed class FileTransactionJournal
{
    private const string PendingFile = ".dlssnr-manager-pending.json";
    private readonly string _gameDir;
    private readonly string _path;
    private FileTransactionState _state;

    private FileTransactionJournal(
        string gameDir,
        FileTransactionState state)
    {
        _gameDir = gameDir;
        _path = Path.Combine(gameDir, PendingFile);
        _state = state;
        Save();
    }

    public static FileTransactionJournal Begin(
        string gameDir,
        string operation,
        string backupDirectory)
        => new(
            gameDir,
            new FileTransactionState(
                Guid.NewGuid().ToString("N"),
                operation,
                backupDirectory,
                DateTimeOffset.UtcNow,
                "PREPARED",
                []));

    public void Stage(string stage)
    {
        _state = _state with { Stage = stage };
        Save();
    }

    public void Track(string path)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(_gameDir, Path.GetFullPath(path));
        }
        catch
        {
            return;
        }

        if (relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            return;

        var files = _state.Files
            .Append(relative)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _state = _state with { Files = files };
        Save();
    }

    public void Commit()
    {
        _state = _state with { Stage = "COMMITTED" };
        Save();
        ClearPending(_gameDir);
    }

    public static FileTransactionState? ReadPending(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, PendingFile);
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<FileTransactionState>(
                File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static void ClearPending(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, PendingFile);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    private void Save()
        => AtomicFile.WriteAllText(
            _path,
            JsonSerializer.Serialize(
                _state,
                new JsonSerializerOptions { WriteIndented = true }));
}
