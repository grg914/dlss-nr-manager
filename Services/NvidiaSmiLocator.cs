namespace DlssNrManager.Services;

/// <summary>
/// Resolves the vendor's executable only from known Windows installation
/// directories. Never executes an arbitrary same-named file from PATH or the
/// manager's working directory.
/// </summary>
public static class NvidiaSmiLocator
{
    public static IReadOnlyList<string> TrustedCandidates()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        return [
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        ];
    }

    public static string? FindInstalled() =>
        TrustedCandidates().FirstOrDefault(File.Exists);
}
