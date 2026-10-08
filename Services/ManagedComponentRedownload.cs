namespace DlssNrManager.Services;

/// <summary>
/// Stashes only explicitly owned component directories, then restores them if
/// the replacement fails. User files, adjacent media engines and license
/// metadata must never be passed to this helper.
/// </summary>
public static class ManagedComponentRedownload
{
    public static async Task ReplaceAsync(
        IReadOnlyList<string> ownedDirectories,
        Func<CancellationToken, Task> install,
        Func<bool> isReady,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownedDirectories);
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(isReady);

        cancellationToken.ThrowIfCancellationRequested();
        var paths = ownedDirectories.Select(Path.GetFullPath).ToArray();
        if (paths.Length == 0 || paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new ArgumentException("Owned directories must be nonempty and unique.", nameof(ownedDirectories));

        // Prevent accidentally stashing an ancestor of a second component.
        for (var i = 0; i < paths.Length; i++)
        for (var j = 0; j < paths.Length; j++)
        {
            if (i != j && paths[j].StartsWith(
                paths[i].TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Overlapping owned directories are unsafe.", nameof(ownedDirectories));
        }

        var transactions = paths.Select(path => new Entry(
            path, path + ".dlssnr-redownload-backup-" + Guid.NewGuid().ToString("N"),
            Directory.Exists(path))).ToArray();

        var started = false;
        try
        {
            foreach (var entry in transactions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.Existed)
                    continue;

                Directory.Move(entry.Path, entry.Backup);
                entry.Moved = true;
            }

            started = true;
            await install(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!isReady())
                throw new InvalidDataException("Replacement component failed post-install validation.");
        }
        catch (Exception original) when (started || transactions.Any(x => x.Moved))
        {
            var rollbackErrors = new List<Exception>();
            foreach (var entry in transactions.Reverse())
            {
                try
                {
                    if (Directory.Exists(entry.Path))
                        Directory.Delete(entry.Path, recursive: true);

                    if (entry.Moved)
                        Directory.Move(entry.Backup, entry.Path);
                }
                catch (Exception error)
                {
                    rollbackErrors.Add(error);
                    AppLogger.Error($"Component rollback failed for {entry.Path}. Backup retained: {entry.Backup}", error);
                }
            }

            if (rollbackErrors.Count != 0)
                throw new AggregateException("Component replacement and rollback both failed. Keep the backups for recovery.",
                    new[] { original }.Concat(rollbackErrors));

            AppLogger.Warn("Component redownload failed; prior component directories restored.");
            throw;
        }

        foreach (var entry in transactions.Where(x => x.Moved))
        {
            try
            {
                Directory.Delete(entry.Backup, recursive: true);
            }
            catch (Exception error)
            {
                // Never invalidate a successfully installed replacement merely
                // because the old backup could not be deleted.
                AppLogger.Warn($"Component updated; old backup could not be removed: {entry.Backup}. {error.Message}");
            }
        }
    }

    private sealed class Entry(string path, string backup, bool existed)
    {
        public string Path { get; } = path;
        public string Backup { get; } = backup;
        public bool Existed { get; } = existed;
        public bool Moved { get; set; }
    }
}
