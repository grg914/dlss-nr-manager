using Xunit;

namespace DlssNrManager.Tests;

public sealed class RealEsrganImmutableReleaseReceiptTests
{
    [Fact]
    public void Release_workflow_requires_12_file_receipt_before_publication()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "DlssNrManager.csproj")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var root = directory!.FullName;
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        var verifier = File.ReadAllText(Path.Combine(root, "tools", "verify-realesrgan-model-candidate.ps1"));

        var prepare = workflow.IndexOf(
            "Verify 12 source-pinned Real-ESRGAN models", StringComparison.Ordinal);
        var receipt = workflow.IndexOf(
            "verify-realesrgan-model-candidate.ps1", StringComparison.Ordinal);
        var publish = workflow.IndexOf("gh release create", StringComparison.Ordinal);
        Assert.True(prepare >= 0 && receipt > prepare && publish > receipt);

        Assert.True(workflow.Split("release-assets/realesrgan-model-manifest.json").Length >= 4,
            "Receipt must be generated, covered by SHA256SUMS, and uploaded.");
        Assert.Contains("[regex]::Matches", verifier);
        Assert.Contains("models.Count -ne 12", verifier);
        Assert.Contains("git hash-object --no-filters", verifier);
        Assert.Contains("Get-FileHash -LiteralPath $file -Algorithm SHA256", verifier);
        Assert.Contains("actualSize -ne $expectedSize", verifier);
    }
}
