namespace DlssNrManager.Services;

public static class MinecraftRenderPipelinePolicy
{
    public const string RequiredBackend = "vulkan";

    private static readonly HashSet<string> ForbiddenManagerRuntimeFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "OptiScaler.ini",
            "OptiScaler.dll",
            "dxgi.dll",
            "d3d12.dll"
        };

    public static IReadOnlyList<string> FindForbiddenManagerRuntimeArtifacts(
        string instanceRoot)
    {
        var root = Path.GetFullPath(instanceRoot);
        var runtimeRoot = Path.Combine(root, ".dlss-nr-manager-runtime");

        if (!Directory.Exists(runtimeRoot))
            return Array.Empty<string>();

        return Directory
            .EnumerateFiles(runtimeRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => ForbiddenManagerRuntimeFiles.Contains(Path.GetFileName(path)))
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void EnsureManagerRuntimeIsNativeNgxOnly(string instanceRoot)
    {
        var forbidden = FindForbiddenManagerRuntimeArtifacts(instanceRoot);
        if (forbidden.Count == 0)
            return;

        throw new InvalidOperationException(
            "The manager-owned Minecraft runtime contains proxy/intermediary files that are not allowed by the native Caustica Vulkan/NGX policy: "
            + string.Join(", ", forbidden)
            + ". Clear the staged Minecraft runtime before retrying.");
    }
}
