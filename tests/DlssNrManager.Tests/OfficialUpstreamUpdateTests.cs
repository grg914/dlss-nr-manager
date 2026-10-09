using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class OfficialUpstreamUpdateTests
{
    [Theory]
    [InlineData("v2.14.1", "v2.14.2", OfficialUpstreamState.NewVersion)]
    [InlineData("v1.2", "v1.2.0", OfficialUpstreamState.UpToDate)]
    [InlineData("v1.2.0.0", "v1.2", OfficialUpstreamState.UpToDate)]
    [InlineData("v1.2", "v1.2.1", OfficialUpstreamState.NewVersion)]
    [InlineData("v1.2.1", "v1.2", OfficialUpstreamState.ReviewRequired)]
    [InlineData("v2.14.1", "v2.14.1", OfficialUpstreamState.UpToDate)]
    [InlineData("n9.0.2", "n9.0.1", OfficialUpstreamState.ReviewRequired)]
    [InlineData("v1.2.0", "malicious-latest", OfficialUpstreamState.ReviewRequired)]
    [InlineData("", "v2.0.0", OfficialUpstreamState.ReviewRequired)]
    [InlineData("v1.0.0", "v2.0.0-rc1", OfficialUpstreamState.ReviewRequired)]
    public void Released_version_comparison_is_conservative(
        string locked, string remote, OfficialUpstreamState expected)
    {
        Assert.Equal(expected, OfficialUpstreamUpdateService.CompareVersion(locked, remote));
    }

    [Fact]
    public void Branch_revision_change_never_claims_approved_upgrade()
    {
        Assert.Equal(OfficialUpstreamState.SourceChanged,
            OfficialUpstreamUpdateService.CompareRevision(new string('a', 40), new string('b', 40)));
        Assert.Equal(OfficialUpstreamState.UpToDate,
            OfficialUpstreamUpdateService.CompareRevision(new string('a', 40), new string('a', 40)));
        Assert.Equal(OfficialUpstreamState.ReviewRequired,
            OfficialUpstreamUpdateService.CompareRevision("unverified", new string('a', 40)));
    }

    [Fact]
    public void Bundled_catalog_preserves_review_policies_and_excludes_unused_mirrors()
    {
        var catalog = OfficialUpstreamUpdateService.GetCatalog();
        Assert.Contains(catalog, x => x.Id == "streamline" &&
            x.Repository == "NVIDIA-RTX/Streamline" && x.LockedTag == "v2.14.1");
        Assert.Contains(catalog, x => x.Id == "nvidia-dlss-sdk" &&
            x.Promotion == "notify-only" && x.LockedRef.Length == 40);
        Assert.Contains(catalog, x => x.Id == "optiscaler" &&
            x.Strategy == "default-branch-head");
        Assert.DoesNotContain(catalog, x => x.Id == "ffmpeg-builds");
        Assert.DoesNotContain(catalog, x => x.Id == "ai-primary");
        Assert.All(catalog, x =>
        {
            Assert.Equal(40, x.LockedRef.Length);
            Assert.Equal(2, x.Repository.Split('/').Length);
        });
    }
}
