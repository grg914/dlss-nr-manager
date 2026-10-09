using System.Text.RegularExpressions;
using Xunit;

namespace DlssNrManager.Tests;

/// <summary>
/// The media installer must not resolve video and FFmpeg from independently
/// changing GitHub /releases/latest responses.
/// </summary>
public sealed class MediaReleaseSnapshotTests
{
    [Fact]
    public void Media_setup_uses_one_release_response_for_both_archives()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "DlssNrManager.csproj")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "Services", "MediaService.cs"));

        var start = source.IndexOf("public async Task SetupAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<string> ProcessAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);

        var setup = source[start..end];
        Assert.Equal(1, Regex.Matches(setup, @"await GetJsonAsync\(").Count);
        Assert.Contains("FindAsset(managerRelease!, ProcessorAsset)", setup);
        Assert.Contains("FindAsset(managerRelease!, FfmpegAsset)", setup);

        // Both digest checks must complete before the first archive download
        // or directory replacement is attempted.
        var processorDigest = setup.IndexOf(
            "string.IsNullOrWhiteSpace(processorAsset!.Sha256)", StringComparison.Ordinal);
        var ffmpegDigest = setup.IndexOf(
            "string.IsNullOrWhiteSpace(ffmpegAsset!.Sha256)", StringComparison.Ordinal);
        var firstDownload = setup.IndexOf("await DownloadAsync(", StringComparison.Ordinal);
        Assert.True(processorDigest > 0 && ffmpegDigest > processorDigest);
        Assert.True(firstDownload > ffmpegDigest);

        Assert.DoesNotContain("GetJsonAsync(FfmpegApi", setup);
        Assert.Contains("asset.Sha256", setup);
    }
}
