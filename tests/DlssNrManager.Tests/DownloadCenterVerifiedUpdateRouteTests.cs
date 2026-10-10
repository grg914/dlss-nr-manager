using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class DownloadCenterVerifiedUpdateRouteTests
{
    [Theory]
    [InlineData(DownloadCenterKind.MediaEngine)]
    [InlineData(DownloadCenterKind.VlcRuntime)]
    [InlineData(DownloadCenterKind.AiUpscale)]
    [InlineData(DownloadCenterKind.AiOriginDetector)]
    public void Confirmed_update_cannot_become_unverified_component_replacement(
        DownloadCenterKind kind)
    {
        Assert.Throws<InvalidOperationException>(() =>
            DownloadCenterService.RequireSupportedVerifiedUpdateRoute(
                kind, requireVerifiedUpdate: true));
    }

    [Theory]
    [InlineData(DownloadCenterKind.MediaEngine)]
    [InlineData(DownloadCenterKind.VlcRuntime)]
    [InlineData(DownloadCenterKind.AiUpscale)]
    [InlineData(DownloadCenterKind.AiOriginDetector)]
    [InlineData(DownloadCenterKind.AiStudioModel)]
    public void Ordinary_repair_does_not_claim_new_version(
        DownloadCenterKind kind)
    {
        DownloadCenterService.RequireSupportedVerifiedUpdateRoute(
            kind, requireVerifiedUpdate: false);
    }

    [Fact]
    public void Approved_ai_studio_route_remains_available_with_package_recheck()
    {
        DownloadCenterService.RequireSupportedVerifiedUpdateRoute(
            DownloadCenterKind.AiStudioModel, requireVerifiedUpdate: true);
    }

    [Fact]
    public void Route_gate_precedes_transactional_replacement()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "DlssNrManager.csproj")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "Services", "DownloadCenterService.cs"));
        var start = source.IndexOf("public async Task RedownloadAsync(", StringComparison.Ordinal);
        var guard = source.IndexOf(
            "RequireSupportedVerifiedUpdateRoute(entry.Kind, requireVerifiedUpdate);",
            start, StringComparison.Ordinal);
        var replacement = source.IndexOf("ManagedComponentRedownload.ReplaceAsync(",
            start, StringComparison.Ordinal);
        Assert.True(start >= 0 && guard > start && replacement > guard);
    }
}
