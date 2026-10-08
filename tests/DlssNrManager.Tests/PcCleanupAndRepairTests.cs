using System.Text;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class PcCleanupServiceTests
{
    [Fact]
    public void Default_cleanup_targets_include_only_rebuildable_cache_groups()
    {
        var service = new PcCleanupService();
        var items = service.CreateDefaultItems();
        var ids = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("user-temp", ids);
        Assert.Contains("windows-temp", ids);
        Assert.Contains("directx-shader", ids);
        Assert.Contains("windows-shell-cache", ids);
        Assert.Contains("nvidia-dx", ids);
        Assert.Contains("nvidia-gl", ids);
        Assert.Contains("nvidia-compute", ids);
        Assert.Contains("nvidia-nv-cache", ids);
        Assert.Contains("amd-shaders", ids);
        Assert.Contains("intel-shaders", ids);

        var allPaths = items.SelectMany(item => item.Paths).ToArray();
        Assert.DoesNotContain(
            allPaths,
            path => path.Contains("Prefetch", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            allPaths,
            path => path.Contains("SoftwareDistribution", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            allPaths,
            path => path.Contains("WinSxS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Pattern_limited_cleanup_does_not_delete_unrelated_or_nested_files()
    {
        var local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(
            local,
            "DlssNrManager.Tests",
            "pc-clean-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "nested");

        Directory.CreateDirectory(nested);

        var matching = Path.Combine(root, "thumbcache_test.db");
        var unrelated = Path.Combine(root, "keep.txt");
        var nestedMatching = Path.Combine(nested, "thumbcache_nested.db");

        await File.WriteAllTextAsync(matching, "cache");
        await File.WriteAllTextAsync(unrelated, "keep");
        await File.WriteAllTextAsync(nestedMatching, "nested");

        try
        {
            var item = new PcCleanupItem
            {
                Id = "test-shell-cache",
                Name = "Test shell cache",
                Description = "Pattern-limited test",
                Paths = [root],
                FilePatterns = ["thumbcache_*.db"],
                Recursive = false,
                DeleteEmptyDirectories = false
            };

            var service = new PcCleanupService();
            await service.AnalyzeAsync([item]);

            Assert.Equal(1, item.FileCount);
            Assert.True(item.Bytes > 0);

            var result = await service.CleanAsync([item]);

            Assert.Equal(1, result.DeletedFiles);
            Assert.False(File.Exists(matching));
            Assert.True(File.Exists(unrelated));
            Assert.True(File.Exists(nestedMatching));
            Assert.True(Directory.Exists(nested));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

public sealed class WindowsRepairServiceTests
{
    [Fact]
    public void Repair_script_runs_dism_before_sfc()
    {
        var script = WindowsRepairService.CreateRepairScript();

        var dism = script.IndexOf(
            "Dism.exe",
            StringComparison.Ordinal);
        var sfc = script.IndexOf(
            "sfc.exe",
            StringComparison.Ordinal);

        Assert.True(dism >= 0);
        Assert.True(sfc > dism);
        Assert.Contains(
            "/Online /Cleanup-Image /RestoreHealth",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "/scannow",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Repair_start_info_uses_uac_and_encoded_static_command()
    {
        var startInfo = WindowsRepairService.CreateStartInfo();

        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb);
        Assert.Contains(
            "-EncodedCommand ",
            startInfo.Arguments,
            StringComparison.Ordinal);

        var encoded = startInfo.Arguments[
            (startInfo.Arguments.IndexOf(
                "-EncodedCommand ",
                StringComparison.Ordinal) + "-EncodedCommand ".Length)..];

        var decoded = Encoding.Unicode.GetString(
            Convert.FromBase64String(encoded));

        Assert.Equal(
            WindowsRepairService.CreateRepairScript(),
            decoded);
    }
}
