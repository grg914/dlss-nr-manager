using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiStudioManagerOwnedModelUpdateTests
{
    private const string OldHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string NewHash =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static AiStudioPackageReceipt Local(string version = "1.0.0", string hash = OldHash) =>
        new("verified-model", version, "ai-studio-verified-model-v1", hash);

    private static AiStudioPackageAvailability Remote(
        string version = "1.1.0",
        string hash = NewHash,
        string packageId = "verified-model") =>
        new("ai-studio-verified-model-v2", "", new AiStudioPackageManifest(
            1, packageId, "Verified", version,
            "ai-studio-verified-model-v2", "models/verified-model",
            "Apache-2.0", "manager-owned",
            new AiStudioPackageArchive("model.zip", 1024, hash, "zip"),
            []),
            new Dictionary<string, AiStudioReleaseAsset>());

    [Fact]
    public void Newer_validated_package_offers_optional_update() =>
        Assert.Equal(MediaUpdateAvailability.UpdateAvailable,
            AiStudioPackageService.EvaluateModelUpdate(Local(), Remote(), true, true));

    [Fact]
    public void Same_or_older_version_does_not_offer_update()
    {
        Assert.Equal(MediaUpdateAvailability.UpToDate,
            AiStudioPackageService.EvaluateModelUpdate(Local("1.1.0"), Remote(), true, true));
        Assert.Equal(MediaUpdateAvailability.UpToDate,
            AiStudioPackageService.EvaluateModelUpdate(Local("2.0.0"), Remote(), true, true));
    }

    [Fact]
    public void Matching_digest_is_not_new_version() =>
        Assert.Equal(MediaUpdateAvailability.UpToDate,
            AiStudioPackageService.EvaluateModelUpdate(Local(), Remote(hash: OldHash), true, true));

    [Fact]
    public void Manually_licensed_model_is_never_offered_automatic_update() =>
        Assert.Equal(MediaUpdateAvailability.UnknownRemoteVersion,
            AiStudioPackageService.EvaluateModelUpdate(Local(), Remote(), true, false));

    [Fact]
    public void Missing_receipt_does_not_offer_update() =>
        Assert.Equal(MediaUpdateAvailability.UnknownLocalVersion,
            AiStudioPackageService.EvaluateModelUpdate(null, Remote(), true, true));

    [Fact]
    public void Offline_or_missing_remote_manifest_does_not_offer_update() =>
        Assert.Equal(MediaUpdateAvailability.UnknownRemoteVersion,
            AiStudioPackageService.EvaluateModelUpdate(Local(), null, true, true));

    [Fact]
    public void Opaque_or_prerelease_version_does_not_offer_update() =>
        Assert.Equal(MediaUpdateAvailability.UnknownRemoteVersion,
            AiStudioPackageService.EvaluateModelUpdate(Local(), Remote("2.0.0-beta"), true, true));

    [Fact]
    public void Unrelated_or_unverified_release_package_does_not_offer_update()
    {
        Assert.Equal(MediaUpdateAvailability.UnknownRemoteVersion,
            AiStudioPackageService.EvaluateModelUpdate(Local(), Remote(packageId: "other"), true, true));
        Assert.Equal(MediaUpdateAvailability.UnknownRemoteVersion,
            AiStudioPackageService.EvaluateModelUpdate(Local(), Remote(hash: "bad"), true, true));
    }

    [Fact]
    public void Uninstalled_model_does_not_offer_update() =>
        Assert.Equal(MediaUpdateAvailability.NotInstalled,
            AiStudioPackageService.EvaluateModelUpdate(Local(), Remote(), false, true));
}
