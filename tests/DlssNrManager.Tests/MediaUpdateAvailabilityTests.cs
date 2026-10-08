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
                State(OldFingerprint), State(NewFingerprint), true));
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

    private static ComponentState State(string? fingerprint) =>
        new("manager:123", 456, DateTimeOffset.UtcNow, fingerprint);
}
