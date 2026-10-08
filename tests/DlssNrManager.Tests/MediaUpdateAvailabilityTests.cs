using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class MediaUpdateAvailabilityTests
{
    private const string OldFingerprint = "video2dlssnr:aaaa|ffmpeg:bbbb";
    private const string NewFingerprint = "video2dlssnr:cccc|ffmpeg:dddd";

    [Fact]
    public void Installed_matching_fingerprint_is_current()
    {
        var local = State(OldFingerprint);
        Assert.Equal(MediaUpdateAvailability.UpToDate,
            ComponentUpdateService.EvaluateMediaUpdate(
                local, State(OldFingerprint.ToUpperInvariant()), true));
    }

    [Fact]
    public void Installed_different_validated_fingerprint_has_optional_update()
    {
        Assert.Equal(MediaUpdateAvailability.UpdateAvailable,
            ComponentUpdateService.EvaluateMediaUpdate(
                State(OldFingerprint, "v3.1.1"), State(NewFingerprint, "v3.2.0"), true));
    }

    [Fact]
    public void Missing_component_never_offers_update()
    {
        Assert.Equal(MediaUpdateAvailability.NotInstalled,
            ComponentUpdateService.EvaluateMediaUpdate(
                State(OldFingerprint), State(NewFingerprint), false));
    }

    [Fact]
    public void Unknown_local_fingerprint_must_not_be_called_outdated()
    {
        Assert.Equal(MediaUpdateAvailability.UnknownLocalVersion,
            ComponentUpdateService.EvaluateMediaUpdate(
                null, State(NewFingerprint), true));
        Assert.Equal(MediaUpdateAvailability.UnknownLocalVersion,
            ComponentUpdateService.EvaluateMediaUpdate(
                State(null), State(NewFingerprint), true));
    }

    [Fact]
    public void Missing_remote_manifest_must_not_offer_update()
    {
        Assert.Equal(MediaUpdateAvailability.UnknownRemoteVersion,
            ComponentUpdateService.EvaluateMediaUpdate(
                State(OldFingerprint), State(null), true));
    }

    [Fact]
    public void Different_hash_from_older_release_must_not_offer_downgrade()
    {
        Assert.Equal(MediaUpdateAvailability.UpToDate,
            ComponentUpdateService.EvaluateMediaUpdate(
                State(OldFingerprint, "v3.2.0"), State(NewFingerprint, "v3.1.1"), true));
    }

    [Fact]
    public void Same_release_tag_different_hash_is_not_called_new_version()
    {
        Assert.Equal(MediaUpdateAvailability.UpToDate,
            ComponentUpdateService.EvaluateMediaUpdate(
                State(OldFingerprint, "v3.2.0"), State(NewFingerprint, "v3.2.0"), true));
    }

    [Fact]
    public void Non_semantic_release_tag_is_not_a_valid_update()
    {
        Assert.Equal(MediaUpdateAvailability.UnknownRemoteVersion,
            ComponentUpdateService.EvaluateMediaUpdate(
                State(OldFingerprint), State(NewFingerprint, "v3.3.0-rc1"), true));
    }

    [Fact]
    public void Legacy_state_without_release_tag_is_unknown()
    {
        Assert.Equal(MediaUpdateAvailability.UnknownLocalVersion,
            ComponentUpdateService.EvaluateMediaUpdate(
                State(OldFingerprint, null), State(NewFingerprint), true));
    }

    private static ComponentState State(string? fingerprint, string? releaseTag = "v3.1.1") =>
        new("manager:123", 456, DateTimeOffset.UtcNow, fingerprint, releaseTag);
}
