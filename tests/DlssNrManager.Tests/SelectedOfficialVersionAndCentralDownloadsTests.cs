using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class SelectedOfficialVersionAndCentralDownloadsTests
{
    private static DownloadCenterService CreateCenter()
    {
        var media = new MediaService();
        return new DownloadCenterService(media, new AiUpscaleService(),
            new LocalAiStudioService(), new AiOriginDetectionService(media),
            new VlcRuntimeService());
    }

    [Theory]
    [InlineData("minecraft-caustica", "caustica", 1)]
    [InlineData("games-streamline", "streamline", 0)]
    [InlineData("games-reshade", "reshade", 0)]
    [InlineData("games-optiscaler", "optiscaler", 0)]
    [InlineData("minecraft-runtime", null, 1)]
    [InlineData("minecraft-spbrscandi", null, 1)]
    [InlineData("minecraft-temurin", null, 1)]
    [InlineData("ai-model:wan2.2-ti2v-5b", "ai-studio-wan2.2", -1)]
    [InlineData("ai-model:flux2-klein-4b", "ai-studio-flux2-reference", -1)]
    [InlineData("media-engine", "video2dlssnr", -1)]
    [InlineData("realesrgan", "realesrgan", -1)]
    [InlineData("vlc-runtime", null, -1)]
    [InlineData("ai-origin-detector", null, -1)]
    public void Only_related_source_is_selected_and_specialized_management_is_preserved(
        string id, string? expectedSource, int expectedMenu)
    {
        var entry = Assert.Single(CreateCenter().GetEntries(), x => x.Id == id);
        Assert.Equal(expectedSource, OfficialUpstreamSelectionPolicy.GetTrackedSourceId(entry));
        Assert.Equal(expectedMenu, OfficialUpstreamSelectionPolicy.GetManagementMenuIndex(entry));
    }

    [Fact]
    public void Every_minecraft_and_game_release_component_is_visible_without_unsafe_auto_install()
    {
        var entries = CreateCenter().GetEntries("fr");
        var ids = new[]
        {
            "minecraft-caustica", "minecraft-runtime", "minecraft-spbrscandi",
            "minecraft-temurin", "games-streamline", "games-reshade", "games-optiscaler"
        };
        foreach (var id in ids)
        {
            var entry = Assert.Single(entries, x => x.Id == id);
            Assert.Equal(DownloadCenterKind.ExternalManaged, entry.Kind);
            Assert.False(entry.IsInstalled);
            Assert.False(entry.CanInstallAutomatically);
            Assert.False(entry.RequiresLicenseAcceptance);
            Assert.True(OfficialUpstreamSelectionPolicy.GetManagementMenuIndex(entry) >= 0);
            Assert.NotEmpty(entry.Details);
        }

        // Keep all existing actionable Media, AI Detection and AI Studio items.
        Assert.Contains(entries, x => x.Id == "media-engine" && x.CanInstallAutomatically);
        Assert.Contains(entries, x => x.Id == "vlc-runtime" && x.CanInstallAutomatically);
        Assert.Contains(entries, x => x.Id == "ai-origin-detector" && x.CanInstallAutomatically);
        Assert.All(LocalAiStudioService.Models,
            model => Assert.Contains(entries, entry => entry.Id == $"ai-model:{model.Id}"));
        Assert.Equal(entries.Count, entries.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public async Task Unmapped_source_is_not_fetched_or_reported_as_an_update()
    {
        var service = new OfficialUpstreamUpdateService();
        var unavailable = await service.CheckSelectedAsync(null);
        Assert.Null(unavailable);
        var unknown = await service.CheckSelectedAsync("minecraft-spbrscandi");
        Assert.Null(unknown);
        Assert.Null(OfficialUpstreamSelectionPolicy.GetTrackedSourceId(null));
        Assert.Equal(-1, OfficialUpstreamSelectionPolicy.GetManagementMenuIndex(null));
    }

    [Fact]
    public void Every_mapped_source_is_a_single_member_of_enabled_official_catalog()
    {
        var allowed = OfficialUpstreamUpdateService.GetCatalog();
        var entries = CreateCenter().GetEntries();
        foreach (var entry in entries)
        {
            var sourceId = OfficialUpstreamSelectionPolicy.GetTrackedSourceId(entry);
            if (sourceId is null) continue;
            var source = Assert.Single(allowed, s => s.Id == sourceId);
            Assert.Equal(2, source.Repository.Split('/').Length);
            Assert.NotEqual("ai-primary", source.Id);
        }
    }

    [Fact]
    public async Task Managed_elsewhere_components_fail_closed_on_generic_installer()
    {
        var center = CreateCenter();
        var caustica = Assert.Single(center.GetEntries(), x => x.Id == "minecraft-caustica");
        await Assert.ThrowsAnyAsync<ArgumentOutOfRangeException>(
            () => center.InstallAsync(caustica));
        Assert.Throws<ArgumentOutOfRangeException>(() => center.Remove(caustica));
        await Assert.ThrowsAnyAsync<ArgumentOutOfRangeException>(
            () => center.RedownloadAsync(caustica));
    }
}
