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

    public static void WriteOptiScalerBuild(
        string gameDir,
        string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        AtomicFile.WriteAllText(
            Path.Combine(gameDir, OptiScalerBuildFile),
            tag.Trim());
    }
}
