namespace DlssNrManager.Services;

/// <summary>
/// Fail-closed directory path validation for manager-owned component and model
/// transactions. A parent junction/symlink may redirect a syntactically safe
/// path outside the manager's storage root.
/// </summary>
public static class ManagedPathSafety
{
    /// <summary>
    /// Prevent recursive component or model removal via a junction/symlink,
    /// including reparse-point ancestors. Refuse deletion rather than follow
    /// an unexpected redirection into an unrelated user directory.
    /// </summary>
    public static void EnsureSafeForRemoval(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (HasReparsePointOnPath(path))
            throw new IOException(
                $"Refusing to remove manager component via junction or symbolic link: {path}");
    }

    public static bool HasReparsePointOnPath(string path)
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
}
