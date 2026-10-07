using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class MinecraftRenderPipelinePolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DlssNrManager.MinecraftPolicy.Tests",
        Guid.NewGuid().ToString("N"));

    public MinecraftRenderPipelinePolicyTests()
        => Directory.CreateDirectory(_root);

    [Fact]
    public void Native_ngx_runtime_accepts_empty_manager_runtime()
    {
        MinecraftRenderPipelinePolicy.EnsureManagerRuntimeIsNativeNgxOnly(_root);

        Assert.Empty(
            MinecraftRenderPipelinePolicy.FindForbiddenManagerRuntimeArtifacts(_root));
    }

    [Theory]
    [InlineData("OptiScaler.ini")]
    [InlineData("OptiScaler.dll")]
    [InlineData("dxgi.dll")]
    [InlineData("d3d12.dll")]
    public void Native_ngx_runtime_rejects_proxy_or_optiscaler_artifacts(string fileName)
    {
        var runtime = Path.Combine(_root, ".dlss-nr-manager-runtime");
        Directory.CreateDirectory(runtime);
        File.WriteAllText(Path.Combine(runtime, fileName), "test");

        var found =
            MinecraftRenderPipelinePolicy.FindForbiddenManagerRuntimeArtifacts(_root);

        Assert.Contains(fileName, found, StringComparer.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() =>
            MinecraftRenderPipelinePolicy.EnsureManagerRuntimeIsNativeNgxOnly(_root));
    }

    [Fact]
    public void Native_ngx_runtime_allows_expected_nvidia_runtime_files()
    {
        var runtime = Path.Combine(_root, ".dlss-nr-manager-runtime");
        Directory.CreateDirectory(runtime);

        foreach (var file in new[]
                 {
                     "sl.interposer.dll",
                     "sl.common.dll",
                     "sl.dlss.dll",
                     "nvngx_dlss.dll",
                     "sl.dlss_g.dll",
                     "nvngx_dlssg.dll",
                     "sl.reflex.dll",
                     "sl.dlss_nr.dll",
                     "nvngx_dlssnr.dll"
                 })
        {
            File.WriteAllText(Path.Combine(runtime, file), "test");
        }

        MinecraftRenderPipelinePolicy.EnsureManagerRuntimeIsNativeNgxOnly(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
        }
    }
}
