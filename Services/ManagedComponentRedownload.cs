namespace DlssNrManager.Services;

public sealed record ComponentRecoveryReport(int Restored, int Failed);

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

        // Guard managed directories against reparse-point redirects.
        foreach (var managedPath in paths)
        {
            if (HasReparsePointOnPath(managedPath))
                throw new IOException($"Unsafe component path: {managedPath}");
        }

        // A previous interrupted transaction must be restored or reviewed
        // before installing again; never create competing backup generations.
        foreach (var path in paths)
        {
            var parent = Path.GetDirectoryName(path);
            if (parent != null && Directory.Exists(parent) &&
                Directory.EnumerateDirectories(
                    parent, Path.GetFileName(path) + ".dlssnr-redownload-backup-*",
                    SearchOption.TopDirectoryOnly).Any())
            {
                throw new InvalidOperationException(
                    $"Unresolved manager-owned backup for {path}. Restart the manager to attempt recovery.");
            }
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

                if (HasReparsePointOnPath(entry.Path) ||
                    HasReparsePointOnPath(entry.Backup))
                    throw new IOException("Component path redirected through a junction or symlink.");

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
                    if (HasReparsePointOnPath(entry.Path) ||
                        HasReparsePointOnPath(entry.Backup))
                        throw new IOException("Unsafe reparse point during rollback; backup retained.");

                    // If a move failed before installation began, do not
                    // touch a sibling component that was never moved.
                    if ((started || entry.Moved) && Directory.Exists(entry.Path))
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
                if (HasReparsePointOnPath(entry.Backup))
                    throw new IOException("Unsafe reparse point in old backup; manual cleanup required.");
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


    /// <summary>
    /// Restores a pre-update component directory after a crash or forced exit.
    /// May roll back an update that actually succeeded but whose backup cleanup
    /// was interrupted; safety of the previous working version takes priority.
    /// Never scans user output, model licenses, or arbitrary folders.
    /// </summary>
    public static ComponentRecoveryReport RecoverKnownManagedComponentBackups()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager");
        var media = Path.Combine(root, "media-engine");
        var owned = new List<string>
        {
            Path.Combine(media, "video2dlssnr"),
            Path.Combine(media, "tools"),
            Path.Combine(media, "realesrgan"),
            Path.Combine(root, "vlc"),
            Path.Combine(root, "ai-origin-detector")
        };
        owned.AddRange(GetManagerOwnedAiStudioRecoveryPaths());

        var transactional = RecoverOwnedBackups(owned);

        var legacy = RecoverLegacyMediaUpdateBackups(media);
        return new ComponentRecoveryReport(
            transactional.Restored + legacy.Restored,
            transactional.Failed + legacy.Failed);
    }

    /// <summary>
    /// Only redistribute-allowed AI Studio models may be automatically
    /// restored. Restricted/manual-license model folders are never selected.
    /// </summary>
    public static IReadOnlyList<string> GetManagerOwnedAiStudioRecoveryPaths()
    {
        var studio = new LocalAiStudioService();
        return LocalAiStudioService.Models
            .Where(model => model.ManagerOwnedRedistributionAllowed)
            .Select(studio.GetModelDirectory)
            .ToArray();
    }

    /// <summary>
    /// Used at process startup, after the single-instance lock is acquired.
    /// Never run while a component install is in progress.
    /// </summary>
    public static ComponentRecoveryReport RecoverOwnedBackups(
        IReadOnlyList<string> ownedDirectories)
    {
        ArgumentNullException.ThrowIfNull(ownedDirectories);
        var restored = 0;
        var failed = 0;

        foreach (var rawPath in ownedDirectories)
        {
            var path = Path.GetFullPath(rawPath);
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                continue;
            if (HasReparsePointOnPath(path))
            {
                failed++;
                AppLogger.Warn($"Skipped component recovery through junction or symlink: {path}");
                continue;
            }

            var prefix = Path.GetFileName(path) + ".dlssnr-redownload-backup-";
            try
            {
                var backups = Directory.EnumerateDirectories(
                        parent, prefix + "*", SearchOption.TopDirectoryOnly)
                    .Where(candidate =>
                    {
                        var name = Path.GetFileName(candidate);
                        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            return false;
                        var suffix = name[prefix.Length..];
                        return suffix.Length == 32 &&
                               suffix.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
                    })
                    .ToArray();

                if (backups.Length == 0)
                    continue;

                if (backups.Length != 1)
                {
                    failed++;
                    AppLogger.Warn(
                        $"Ambiguous recovery backups for {path}: {backups.Length}. Manual review required.");
                    continue;
                }

                var backup = backups[0];
                if (IsReparsePoint(backup) ||
                    (Directory.Exists(path) && IsReparsePoint(path)))
                {
                    failed++;
                    AppLogger.Warn($"Skipped junction or symlink while recovering {path}.");
                    continue;
                }

                var quarantine = path + ".dlssnr-recovery-quarantine-" +
                                 Guid.NewGuid().ToString("N");
                var quarantined = false;
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Move(path, quarantine);
                        quarantined = true;
                    }

                    Directory.Move(backup, path);
                    restored++;
                    AppLogger.Warn($"Restored previous manager-owned component after interrupted update: {path}.");

                    if (quarantined)
                    {
                        try { Directory.Delete(quarantine, recursive: true); }
                        catch (Exception cleanup)
                        {
                            AppLogger.Warn(
                                $"Restored component but left partial replacement for manual cleanup: {quarantine}. {cleanup.Message}");
                        }
                    }
                }
                catch (Exception restoreError)
                {
                    failed++;
                    AppLogger.Error($"Interrupted component recovery failed for {path}; backup preserved.", restoreError);
                    if (quarantined && !Directory.Exists(path) && Directory.Exists(quarantine))
                    {
                        try { Directory.Move(quarantine, path); }
                        catch (Exception rollbackError)
                        {
                            AppLogger.Error($"Could not return unfinished component directory {quarantine}.",
                                rollbackError);
                        }
                    }
                }
            }
            catch (Exception error) when (error is IOException or
                                          UnauthorizedAccessException or
                                          ArgumentException)
            {
                failed++;
                AppLogger.Error($"Unable to inspect interrupted component backup for {path}.", error);
            }
        }

        return new ComponentRecoveryReport(restored, failed);
    }

    /// <summary>
    /// Migrates pre-v4 MediaService _update-backup-GUID folders into the same
    /// per-component transaction scheme as Redownload. Directory.Move is
    /// atomic on the volume; if power is lost mid-migration, the next
    /// startup can resume from either the converted or the legacy folder.
    /// Unknown entries and ambiguous backups are never deleted.
    /// </summary>
    public static ComponentRecoveryReport RecoverLegacyMediaUpdateBackups(string mediaRoot)
    {
        mediaRoot = Path.GetFullPath(mediaRoot);
        if (!Directory.Exists(mediaRoot))
            return new ComponentRecoveryReport(0, 0);
        if (HasReparsePointOnPath(mediaRoot))
        {
            AppLogger.Warn($"Skipped legacy media recovery through junction or symlink: {mediaRoot}");
            return new ComponentRecoveryReport(0, 1);
        }

        var rootPrefix = "_update-backup-";
        var legacy = Directory
            .EnumerateDirectories(mediaRoot, rootPrefix + "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                var suffix = name[rootPrefix.Length..];
                return suffix.Length == 32 && suffix.All(c =>
                    c is >= '0' and <= '9' or >= 'a' and <= 'f');
            })
            .ToArray();

        if (legacy.Length == 0)
            return new ComponentRecoveryReport(0, 0);

        // Do not guess between multiple generations or silently overwrite the
        // last known working component.
        if (legacy.Length > 1)
        {
            AppLogger.Warn($"Multiple legacy media update backups found in {mediaRoot}; manual review required.");
            return new ComponentRecoveryReport(0, 1);
        }

        var legacyRoot = legacy[0];
        if (IsReparsePoint(legacyRoot))
        {
            AppLogger.Warn($"Refused to follow symlink or junction media backup {legacyRoot}.");
            return new ComponentRecoveryReport(0, 1);
        }

        var targetPaths = new[]
        {
            Path.Combine(mediaRoot, "video2dlssnr"),
            Path.Combine(mediaRoot, "tools")
        };

        var migrationFailed = 0;
        foreach (var target in targetPaths)
        {
            var source = Path.Combine(legacyRoot, Path.GetFileName(target));
            if (!Directory.Exists(source))
                continue;

            var movedName = Path.GetFileName(legacyRoot)[rootPrefix.Length..];
            var converted = target + ".dlssnr-redownload-backup-" + movedName;

            try
            {
                if (IsReparsePoint(source) ||
                    (Directory.Exists(target) && IsReparsePoint(target)) ||
                    Directory.Exists(converted))
                {
                    throw new IOException("An unsafe or conflicting media backup path exists.");
                }

                // If a different backup already exists, defer to manual
                // recovery rather than introducing two possible originals.
                if (Directory.EnumerateDirectories(
                        mediaRoot,
                        Path.GetFileName(target) + ".dlssnr-redownload-backup-*",
                        SearchOption.TopDirectoryOnly).Any())
                {
                    throw new IOException("Another transactional backup exists.");
                }

                Directory.Move(source, converted);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                migrationFailed++;
                AppLogger.Error($"Unable to migrate legacy media backup {source}; left intact.", ex);
            }
        }

        // Reuse the existing conservative recovery and the same tests for
        // component swap/verification semantics.
        var recovered = RecoverOwnedBackups(targetPaths);

        // Clean up an empty legacy container only; never recursively remove
        // unknown files or an incomplete component backup.
        try
        {
            if (Directory.Exists(legacyRoot))
            {
                if (!Directory.EnumerateFileSystemEntries(legacyRoot).Any())
                    Directory.Delete(legacyRoot, recursive: false);
                else
                {
                    migrationFailed++;
                    AppLogger.Warn($"Legacy media backup is not empty; manual review required: {legacyRoot}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            migrationFailed++;
            AppLogger.Error($"Could not clean empty legacy backup container {legacyRoot}.", ex);
        }

        return new ComponentRecoveryReport(
            recovered.Restored,
            recovered.Failed + migrationFailed);
    }

    // A parent junction can redirect an apparently managed path outside the
    // application's owned directories; inspect all existing ancestors.
    private static bool HasReparsePointOnPath(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) ||
                string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }

        return false;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private sealed class Entry(string path, string backup, bool existed)
    {
        public string Path { get; } = path;
        public string Backup { get; } = backup;
        public bool Existed { get; } = existed;
        public bool Moved { get; set; }
    }
}
