using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class VlcCandidatePreferenceTests
{
    private static VlcVideoEnhancementStatus Candidate(string path, Version? version)
        => new(true, path, version,
            version != null && version >= new Version(3, 0, 24),
            "fixture");

    [Fact]
    public void New_verified_VLC_wins_even_when_legacy_path_is_first()
    {
        var result = VlcVideoEnhancementService.ChoosePreferredCandidate(
            [Candidate("legacy", new Version(3, 0, 23)),
             Candidate("managed", new Version(3, 0, 24))]);
        Assert.Equal("managed", result.ExecutablePath);
        Assert.True(result.SupportsD3d11EnhancementOptions);
    }

    [Fact]
    public void Unverified_metadata_does_not_override_known_compatible_VLC()
    {
        var chosen = VlcVideoEnhancementService.ChoosePreferredCandidate(
            [Candidate("unknown", null),
             Candidate("system", new Version(3, 0, 24))]);
        Assert.Equal("system", chosen.ExecutablePath);
    }

    [Fact]
    public void No_verified_compatible_VLC_fails_closed()
    {
        var chosen = VlcVideoEnhancementService.ChoosePreferredCandidate(
            [Candidate("unknown", null)]);
        Assert.True(chosen.Found);
        Assert.False(chosen.SupportsD3d11EnhancementOptions);
        Assert.Null(chosen.Version);
    }

    [Fact]
    public void Picks_newest_verified_VLC_among_valid_candidates()
    {
        var chosen = VlcVideoEnhancementService.ChoosePreferredCandidate(
            [Candidate("v3024", new Version(3, 0, 24)),
             Candidate("v3025", new Version(3, 0, 25))]);
        Assert.Equal("v3025", chosen.ExecutablePath);
    }

    [Fact]
    public void Empty_candidate_set_is_not_found()
    {
        var chosen = VlcVideoEnhancementService.ChoosePreferredCandidate([]);
        Assert.False(chosen.Found);
        Assert.False(chosen.SupportsD3d11EnhancementOptions);
    }
}
