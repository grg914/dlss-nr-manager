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
    [Fact]
    public void Confirmed_update_rejects_equal_or_older_or_unverified_model_packages()
    {
        AiStudioPackageService.RequireConfirmedModelUpgrade(
            Local(), Remote(), installed: true,
            automaticRedistributionAllowed: true);

        foreach (var candidate in new[]
        {
            Remote(version: "1.0.0"),
            Remote(version: "0.9.0"),
            Remote(hash: OldHash),
            Remote(version: "2.0.0-beta"),
            Remote(hash: "bad"),
            Remote(packageId: "other")
        })
        {
            Assert.Throws<InvalidOperationException>(() =>
                AiStudioPackageService.RequireConfirmedModelUpgrade(
                    Local(), candidate, installed: true,
                    automaticRedistributionAllowed: true));
        }

        Assert.Throws<InvalidOperationException>(() =>
            AiStudioPackageService.RequireConfirmedModelUpgrade(
                null, Remote(), installed: true,
                automaticRedistributionAllowed: true));
        Assert.Throws<InvalidOperationException>(() =>
            AiStudioPackageService.RequireConfirmedModelUpgrade(
                Local(), Remote(), installed: false,
                automaticRedistributionAllowed: true));
        Assert.Throws<InvalidOperationException>(() =>
            AiStudioPackageService.RequireConfirmedModelUpgrade(
                Local(), Remote(), installed: true,
                automaticRedistributionAllowed: false));
    }

    [Fact]
    public void Download_center_update_must_revalidate_package_before_swap()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(
            Path.Combine(root.FullName, "DlssNrManager.csproj")))
            root = root.Parent;
        Assert.NotNull(root);
        var services = Path.Combine(root!.FullName, "Services");
        var install = File.ReadAllText(Path.Combine(
            services, "AiStudioPackageService.cs"));
        var download = File.ReadAllText(Path.Combine(
            services, "DownloadCenterService.cs"));
        var window = File.ReadAllText(Path.Combine(
            root.FullName, "MainWindow.xaml.cs"));

        var initial = install.IndexOf("if (requireVerifiedUpdate)", StringComparison.Ordinal);
        var approved = install.IndexOf("await LargeDownloadApprovalHub.EnsureApprovedAsync(", StringComparison.Ordinal);
        var beforeSwap = install.IndexOf(
            "if (requireVerifiedUpdate)", approved, StringComparison.Ordinal);
        var swap = install.IndexOf(
            "await ManagedComponentRedownload.ReplaceAsync(", StringComparison.Ordinal);
        Assert.True(initial >= 0 && initial < approved);
        Assert.True(beforeSwap > approved && beforeSwap < swap);
        Assert.Contains("requireVerifiedUpdate: isModelUpdate", window);
        Assert.Contains("requireVerifiedUpdate);", download);
    }

}
