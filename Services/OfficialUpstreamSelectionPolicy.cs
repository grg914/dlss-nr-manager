namespace DlssNrManager.Services;

/// <summary>
/// Maps a Downloads selection to the one relevant official upstream repository.
/// This does not establish that the published source commit is a compatible
/// or redistributable model package. Unmapped components never trigger a
/// global scan of unrelated repositories.
/// </summary>
public static class OfficialUpstreamSelectionPolicy
{
    // Specialized installers require their original game/Minecraft context.
    // MainMenuList is the existing public V4 sidebar navigation.
    public static int GetManagementMenuIndex(DownloadCenterEntry? selected)
    {
        if (selected?.Kind != DownloadCenterKind.ExternalManaged)
            return -1;

        if (selected.Id.StartsWith("minecraft-", StringComparison.Ordinal))
            return 1; // Minecraft RTX
        if (selected.Id is "games-streamline" or "games-reshade" or "games-optiscaler")
            return 0; // Jeux & DLSS
        return -1;
    }

    public static string? GetTrackedSourceId(DownloadCenterEntry? selected)
    {
        if (selected is null)
            return null;

        return selected.Id switch
        {
            "minecraft-caustica" => "caustica",
            "games-streamline" => "streamline",
            "games-reshade" => "reshade",
            "games-optiscaler" => "optiscaler",
            "media-engine" => "video2dlssnr",
            "realesrgan" => "realesrgan",
            "ai-model:flux2-klein-4b" => "ai-studio-flux2-reference",
            "ai-model:flux2-dev" => "ai-studio-flux2-reference",
            "ai-model:wan2.2-ti2v-5b" => "ai-studio-wan2.2",
            "ai-model:wan2.2-t2v-a14b" => "ai-studio-wan2.2",
            "ai-model:wan2.2-i2v-a14b" => "ai-studio-wan2.2",
            "ai-model:wan2.2-animate-14b" => "ai-studio-wan2.2",
            _ => null
        };
    }
}
