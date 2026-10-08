using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class PcCleanupParentJunctionTests
{
    [Fact]
    public async Task Cleanup_rejects_whitelisted_path_under_symlink_parent()
    {
        var local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var link = Path.Combine(local,
            "DlssNrManager-PcClean-Link-" + Guid.NewGuid().ToString("N"));
        var external = Path.Combine(Path.GetTempPath(),
            "DlssNrManager-PcClean-Outside-" + Guid.NewGuid().ToString("N"));
        var externalCache = Path.Combine(external, "cache");
        Directory.CreateDirectory(externalCache);
        var sentinel = Path.Combine(externalCache, "keep-user-data.tmp");
        File.WriteAllText(sentinel, "must not be deleted");

        var linked = false;
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, external);
                linked = true;
            }
            catch (UnauthorizedAccessException) { return; }
            catch (IOException) { return; }
            catch (PlatformNotSupportedException) { return; }

            var item = new PcCleanupItem
            {
                Id = "test-parent-junction",
                Name = "Temporary test cache",
                Description = "Owned test fixture",
                Paths = [Path.Combine(link, "cache")]
            };
            var service = new PcCleanupService();

            var scan = await service.AnalyzeAsync([item]);
            Assert.Equal(0, scan[0].FileCount);
            Assert.True(scan[0].SkippedCount >= 1);

            var result = await service.CleanAsync([item]);
            Assert.Equal(0, result.DeletedFiles);
            Assert.True(result.SkippedFiles >= 1);
            Assert.Equal("must not be deleted", File.ReadAllText(sentinel));
        }
        finally
        {
            if (linked && Directory.Exists(link))
                Directory.Delete(link, recursive: false);
            if (Directory.Exists(external))
                Directory.Delete(external, recursive: true);
        }
    }

    [Fact]
    public async Task Cleanup_still_deletes_only_selected_files_in_normal_allowed_directory()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager-PcClean-Normal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var shouldDelete = Path.Combine(root, "old.tmp");
        var shouldKeep = Path.Combine(root, "important.txt");
        File.WriteAllText(shouldDelete, "cache");
        File.WriteAllText(shouldKeep, "user data");

        try
        {
            var item = new PcCleanupItem
            {
                Id = "test-safe-cache",
                Name = "Test cache",
                Description = "Only *.tmp",
                Paths = [root],
                FilePatterns = ["*.tmp"],
                Recursive = false,
                DeleteEmptyDirectories = false
            };
            var result = await new PcCleanupService().CleanAsync([item]);
            Assert.Equal(1, result.DeletedFiles);
            Assert.False(File.Exists(shouldDelete));
            Assert.Equal("user data", File.ReadAllText(shouldKeep));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
