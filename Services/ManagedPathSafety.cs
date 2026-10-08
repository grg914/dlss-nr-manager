namespace DlssNrManager.Services;

/// <summary>
/// Fail-closed directory path validation for manager-owned component and model
/// transactions. A parent junction/symlink may redirect a syntactically safe
/// path outside the manager's storage root.
/// </summary>
internal static class ManagedPathSafety
{
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
