using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class DownloadManagerTests
{
    [Fact]
    public async Task Large_download_approval_is_not_requested_at_or_below_one_gib()
    {
        var called = false;
        LargeDownloadApprovalHub.ApprovalRequested =
            (_, _) =>
            {
                called = true;
                return Task.FromResult(false);
            };

        try
        {
            await LargeDownloadApprovalHub.EnsureApprovedAsync(
                "test",
                1024L * 1024 * 1024,
                "test");

            Assert.False(called);
        }
        finally
        {
            LargeDownloadApprovalHub.ApprovalRequested = null;
        }
    }

    [Fact]
    public async Task Large_download_approval_is_required_above_one_gib()
    {
        LargeDownloadApprovalRequest? request = null;

        LargeDownloadApprovalHub.ApprovalRequested =
            (value, _) =>
            {
                request = value;
                return Task.FromResult(true);
            };

        try
        {
            var size =
                5L * 1024 * 1024 * 1024;

            await LargeDownloadApprovalHub.EnsureApprovedAsync(
                "Wan2.2",
                size,
                "AI Studio model");

            Assert.NotNull(request);
            Assert.Equal(size, request!.TotalBytes);
            Assert.Equal("Wan2.2", request.Label);
        }
        finally
        {
            LargeDownloadApprovalHub.ApprovalRequested = null;
        }
    }

    [Fact]
    public async Task Rejected_large_download_is_cancelled_before_transfer()
    {
        LargeDownloadApprovalHub.ApprovalRequested =
            (_, _) => Task.FromResult(false);

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => LargeDownloadApprovalHub.EnsureApprovedAsync(
                    "Huge model",
                    2L * 1024 * 1024 * 1024,
                    "AI Studio model"));
        }
        finally
        {
            LargeDownloadApprovalHub.ApprovalRequested = null;
        }
    }

    [Fact]
    public void Download_center_contains_media_and_all_ai_model_entries()
    {
        var studio = new LocalAiStudioService();
        var media = new MediaService();
        var center = new DownloadCenterService(
            media,
            new AiUpscaleService(),
            studio,
            new AiOriginDetectionService(media),
            new VlcRuntimeService());

        var entries = center.GetEntries();

        Assert.Contains(
            entries,
            x => x.Id == "media-engine");

        Assert.Contains(
            entries,
            x => x.Id == "realesrgan");

        Assert.Contains(
            entries,
            x => x.Id == "vlc-runtime");

        Assert.Contains(
            entries,
            x => x.Id == "ai-origin-detector");

        foreach (var model in LocalAiStudioService.Models)
        {
            Assert.Contains(
                entries,
                x => x.Id == $"ai-model:{model.Id}");
        }
    }

    [Fact]
    public void Restricted_models_are_visible_but_not_auto_installable()
    {
        var media = new MediaService();
        var center = new DownloadCenterService(
            media,
            new AiUpscaleService(),
            new LocalAiStudioService(),
            new AiOriginDetectionService(media),
            new VlcRuntimeService());

        var entries = center.GetEntries();

        var fluxDev =
            Assert.Single(
                entries,
                x => x.Id == "ai-model:flux2-dev");

        var qwen =
            Assert.Single(
                entries,
                x => x.Id == "ai-model:qwen-image-2.1");

        Assert.False(
            fluxDev.CanInstallAutomatically);
        Assert.True(
            fluxDev.RequiresLicenseAcceptance);

        Assert.False(
            qwen.CanInstallAutomatically);
        Assert.True(
            qwen.RequiresLicenseAcceptance);
    }
    [Fact]
    public void Manual_license_metadata_is_registered_for_restricted_models()
    {
        foreach (var id in new[]
                 {
                     "flux2-dev",
                     "qwen-image-2.1",
                     "ltx-2.5"
                 })
        {
            var model =
                Assert.Single(
                    LocalAiStudioService.Models,
                    x => x.Id == id);

            var info =
                AiStudioLicenseAcceptanceService.GetInfo(
                    model);

            Assert.NotNull(info);
            Assert.False(
                string.IsNullOrWhiteSpace(
                    info!.OfficialLicenseUrl));
        }
    }

}
