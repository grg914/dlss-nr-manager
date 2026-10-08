using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiStudioModelPackageSafetyTests
{
    [Fact]
    public void Package_can_only_install_to_exact_selected_model_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-studio-test-root");
        var selected = Path.Combine(root, "models", "flux2-klein-4b");
        var actual = AiStudioPackageService.ValidateModelInstallTarget(selected, selected);
        Assert.Equal(Path.GetFullPath(selected), actual);
    }

    [Theory]
    [InlineData("models/flux2-dev")]
    [InlineData("models/qwen-image-2.1")]
    [InlineData("outputs")]
    [InlineData("jobs")]
    [InlineData("runtime")]
    [InlineData("workflows")]
    public void Package_cannot_overwrite_other_models_or_workspace_data(string relative)
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-studio-test-root");
        var requested = Path.Combine(root, relative);
        var selected = Path.Combine(root, "models", "flux2-klein-4b");
        Assert.Throws<InvalidDataException>(() =>
            AiStudioPackageService.ValidateModelInstallTarget(requested, selected));
    }

    [Fact]
    public void Startup_recovery_list_contains_only_redistributable_models()
    {
        var studio = new LocalAiStudioService();
        var paths = ManagedComponentRedownload.GetManagerOwnedAiStudioRecoveryPaths();
        var allowed = LocalAiStudioService.Models
            .Where(model => model.ManagerOwnedRedistributionAllowed)
            .Select(studio.GetModelDirectory)
            .ToArray();
        var restricted = LocalAiStudioService.Models
            .Where(model => !model.ManagerOwnedRedistributionAllowed)
            .Select(studio.GetModelDirectory)
            .ToArray();

        Assert.Equal(allowed.Length, paths.Count);
        Assert.All(allowed, expected =>
            Assert.Contains(paths, p => string.Equals(
                p, expected, StringComparison.OrdinalIgnoreCase)));
        Assert.All(restricted, disallowed =>
            Assert.DoesNotContain(paths, p => string.Equals(
                p, disallowed, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(paths, p =>
            string.Equals(p, studio.OutputsRoot, StringComparison.OrdinalIgnoreCase));
    }
}
