namespace DlssNrManager.Services;

/// <summary>
/// Commits an explicitly selected manual-license model import without
/// deleting the old model if the initial backup move fails. This deliberately
/// does NOT register restricted weights for unattended startup recovery.
/// </summary>
public static class ManualModelDirectorySwap
{
    public static void ReplaceStaged(
        string staging,
        string target,
        Action<string, string>? moveDirectory = null)
    {
        var stagePath = Path.GetFullPath(staging);
        var targetPath = Path.GetFullPath(target);
        if (!Directory.Exists(stagePath))
            throw new DirectoryNotFoundException("Prepared model import directory is missing.");

        var parent = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidDataException("Model destination has no parent.");
        if (!string.Equals(Path.GetDirectoryName(stagePath), parent,
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(stagePath).StartsWith(
                Path.GetFileName(targetPath) + ".staging-",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Model staging directory is not a sibling of the selected model.");
        }

        if (ManagedPathSafety.HasReparsePointOnPath(stagePath) ||
            ManagedPathSafety.HasReparsePointOnPath(targetPath))
            throw new InvalidDataException("Model import refused for symbolic links or junctions.");

        // Legacy imports may have left the only working model in a backup.
        // Never overwrite it or create multiple competing generations.
        if (Directory.EnumerateDirectories(
                parent, Path.GetFileName(targetPath) + ".backup-*",
                SearchOption.TopDirectoryOnly).Any())
        {
            throw new InvalidOperationException(
                "An unresolved manual model backup exists. Review it before importing another copy.");
        }

        moveDirectory ??= Directory.Move;
        var backupPath = targetPath + ".backup-" + Guid.NewGuid().ToString("N");
        var movedOriginal = false;
        try
        {
            if (ManagedPathSafety.HasReparsePointOnPath(backupPath))
                throw new InvalidDataException("Model backup path traverses a junction or symbolic link.");
            if (Directory.Exists(targetPath))
            {
                moveDirectory(targetPath, backupPath);
                movedOriginal = true;
            }

            if (ManagedPathSafety.HasReparsePointOnPath(stagePath) ||
                ManagedPathSafety.HasReparsePointOnPath(targetPath))
                throw new InvalidDataException("Model path redirected during import.");
            moveDirectory(stagePath, targetPath);
        }
        catch (Exception installError)
        {
            if (movedOriginal)
            {
                try
                {
                    if (ManagedPathSafety.HasReparsePointOnPath(targetPath) ||
                        ManagedPathSafety.HasReparsePointOnPath(backupPath))
                        throw new InvalidDataException("Unsafe model rollback path; backup retained.");
                    if (Directory.Exists(targetPath))
                        Directory.Delete(targetPath, recursive: true);

                    if (Directory.Exists(backupPath))
                        Directory.Move(backupPath, targetPath);
                }
                catch (Exception restoreError)
                {
                    AppLogger.Error(
                        $"Manual AI Studio import rollback failed. Backup preserved: {backupPath}",
                        restoreError);
                    throw new AggregateException(
                        "Manual model import and rollback failed. Keep the backup for manual recovery.",
                        installError, restoreError);
                }
            }

            throw;
        }

        if (movedOriginal)
        {
            try
            {
                if (ManagedPathSafety.HasReparsePointOnPath(backupPath))
                    throw new InvalidDataException("Unsafe backup path; manual cleanup required.");
                Directory.Delete(backupPath, recursive: true);
            }
            catch (Exception error)
            {
                // This is backup cleanup AFTER a completed installation. A
                // cleanup error must never delete the new installed model.
                AppLogger.Warn(
                    $"Manual model installed, but the previous backup could not be removed: {backupPath}. {error.Message}");
            }
        }
    }

}
