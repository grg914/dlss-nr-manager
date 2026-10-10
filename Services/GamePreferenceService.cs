using DlssNrManager.Models;

namespace DlssNrManager.Services;

public static class GamePreferenceService
{
    private const string OptiScalerBuildFile =
        ".dlssnr-manager-optiscaler-build";

    public static string? ReadOptiScalerBuild(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, OptiScalerBuildFile);
            return File.Exists(path)
                ? File.ReadAllText(path).Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }

    // The historical preference stored only the upstream tag. From V4,
    // identical tags can identify DIFFERENT native DLL archives, so persist
    // the selected approved release URL and accept old tags on read only.
    internal static bool MatchesOptiScalerBuild(
        ReleaseInfo release,
        string? storedSelection) =>
        !string.IsNullOrWhiteSpace(storedSelection) &&
        (
            release.ZipUrl.Equals(
                storedSelection, StringComparison.OrdinalIgnoreCase) ||
            (!Uri.TryCreate(storedSelection, UriKind.Absolute, out _) &&
             release.Tag.Equals(
                 storedSelection, StringComparison.OrdinalIgnoreCase))
        );

    public static void WriteOptiScalerBuild(
        string gameDir,
        string selectionKey)
    {
        if (string.IsNullOrWhiteSpace(selectionKey))
            return;

        AtomicFile.WriteAllText(
            Path.Combine(gameDir, OptiScalerBuildFile),
            selectionKey.Trim());
    }
}
