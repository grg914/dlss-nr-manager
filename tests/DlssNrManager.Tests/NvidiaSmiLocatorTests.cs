using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class NvidiaSmiLocatorTests
{
    [Fact]
    public void Candidate_paths_are_absolute_explicit_NVIDIA_locations()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Empty(NvidiaSmiLocator.TrustedCandidates());
            return;
        }

        var candidates = NvidiaSmiLocator.TrustedCandidates();
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, path =>
        {
            Assert.True(Path.IsPathFullyQualified(path));
            Assert.EndsWith("nvidia-smi.exe", path, StringComparison.OrdinalIgnoreCase);
        });
        Assert.StartsWith(Environment.SystemDirectory, candidates[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("NVIDIA Corporation", "NVSMI"), candidates[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolver_only_returns_one_of_the_trusted_candidates()
    {
        var resolved = NvidiaSmiLocator.FindInstalled();
        if (resolved == null)
            return;

        Assert.Contains(NvidiaSmiLocator.TrustedCandidates(),
            candidate => string.Equals(candidate, resolved, StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(resolved));
    }
}
