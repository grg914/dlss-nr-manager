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
    public void Selected_model_under_redirected_parent_is_rejected_before_archive_extraction()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "dlssnr-ai-staging-safety-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(root, "unmanaged");
        var sentinel = Path.Combine(outside, "KEEP.txt");
        var redirect = Path.Combine(root, "redirected-models");
        try
        {
            Directory.CreateDirectory(outside);
            File.WriteAllText(sentinel, "must remain intact");

            try { Directory.CreateSymbolicLink(redirect, outside); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or PlatformNotSupportedException)
            {
                return; // Symlink permissions depend on Windows CI host policy.
            }

            var selected = Path.Combine(redirect, "selected-model");
            Assert.Throws<IOException>(() =>
                AiStudioPackageService.ValidateModelInstallTarget(selected, selected));
            Assert.Equal("must remain intact", File.ReadAllText(sentinel));
        }
        finally
        {
            try
            {
                if (Directory.Exists(redirect))
                    Directory.Delete(redirect);
            }
            catch { }
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public void Selected_model_is_rejected_if_target_itself_is_symlink()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "dlssnr-ai-direct-link-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(root, "keep-user-data");
        var modelLink = Path.Combine(root, "models", "model");
        try
        {
            Directory.CreateDirectory(outside);
            Directory.CreateDirectory(Path.GetDirectoryName(modelLink)!);
            File.WriteAllText(Path.Combine(outside, "KEEP.txt"), "safe");
            try { Directory.CreateSymbolicLink(modelLink, outside); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or PlatformNotSupportedException)
            {
                return;
            }

            Assert.Throws<IOException>(() =>
                AiStudioPackageService.ValidateModelInstallTarget(modelLink, modelLink));
            Assert.Equal("safe", File.ReadAllText(Path.Combine(outside, "KEEP.txt")));
        }
        finally
        {
            try { if (Directory.Exists(modelLink)) Directory.Delete(modelLink); }
            catch { }
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
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
