using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class MediaReleaseIdentityTests
{
    [Theory]
    [InlineData("v3.2.0", 123L, 456L, true)]
    [InlineData("v3.2.1", 123L, 456L, false)]
    [InlineData("v3.1.1", 123L, 456L, false)]
    [InlineData("v3.2.0", 124L, 456L, false)]
    [InlineData("v3.2.0", 123L, 457L, false)]
    [InlineData("v3.2.0", 0L, 456L, false)]
    [InlineData("v3.2.0", 123L, 0L, false)]
    [InlineData(null, 123L, 456L, false)]
    public void Media_install_must_match_preflight_tag_and_both_asset_ids(
        string? actualTag, long processorId, long ffmpegId, bool expected)
    {
        var approved = new ComponentState(
            "manager:123", 456L, DateTimeOffset.UtcNow,
            "video2dlssnr:AAAA|ffmpeg:BBBB", "v3.2.0");

        Assert.Equal(expected,
            MediaService.IsApprovedMediaSelection(
                approved, actualTag, processorId, ffmpegId));
    }

    [Fact]
    public void Legacy_installation_receipt_cannot_approve_a_new_release()
    {
        var noTag = new ComponentState(
            "manager:123", 456L, DateTimeOffset.UtcNow, null, null);
        Assert.False(MediaService.IsApprovedMediaSelection(
            noTag, "v3.2.0", 123L, 456L));
    }

    [Fact]
    public void Preflight_receipt_must_be_carried_into_the_transaction()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "DlssNrManager.csproj")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var service = File.ReadAllText(Path.Combine(
            directory!.FullName, "Services", "ComponentUpdateService.cs"));
        var media = File.ReadAllText(Path.Combine(
            directory.FullName, "Services", "MediaService.cs"));

        Assert.Contains(
            "await media.UpdateToolsAsync(progress, cancellationToken, remote);",
            service);
        Assert.Contains("token => SetupAsync(progress, token, approvedRelease)", media);

        var start = media.IndexOf("public async Task SetupAsync(", StringComparison.Ordinal);
        var end = media.IndexOf("public async Task<string> ProcessAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var setup = media[start..end];
        var approvalCheck = setup.IndexOf("IsApprovedMediaSelection(", StringComparison.Ordinal);
        var firstDownload = setup.IndexOf("await DownloadAsync(", StringComparison.Ordinal);
        Assert.True(approvalCheck >= 0 && firstDownload > approvalCheck);
    }
}
