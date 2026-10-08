using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class ManualModelDirectorySwapTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "DlssNrManager.ManualImport.Tests",
        Guid.NewGuid().ToString("N"));

    public ManualModelDirectorySwapTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Successful_import_replaces_only_the_model_and_cleans_backup()
    {
        var (staging, target) = CreateFolders();
        ManualModelDirectorySwap.ReplaceStaged(staging, target);
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "weights.bin")));
        Assert.Empty(Directory.GetDirectories(_root, "model.backup-*"));
    }

    [Fact]
    public void Failure_on_first_move_leaves_original_model_untouched()
    {
        var (staging, target) = CreateFolders();
        Assert.Throws<IOException>(() => ManualModelDirectorySwap.ReplaceStaged(
            staging, target, (_, _) => throw new IOException("Move denied")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "weights.bin")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(staging, "weights.bin")));
    }

    [Fact]
    public void Failure_after_original_was_moved_restores_original_model()
    {
        var (staging, target) = CreateFolders();
        var calls = 0;
        Assert.Throws<IOException>(() => ManualModelDirectorySwap.ReplaceStaged(
            staging, target, (source, destination) =>
            {
                calls++;
                if (calls == 2)
                    throw new IOException("Stage move failed");
                Directory.Move(source, destination);
            }));
        Assert.Equal(2, calls);
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "weights.bin")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(staging, "weights.bin")));
        Assert.Empty(Directory.GetDirectories(_root, "model.backup-*"));
    }

    [Fact]
    public void Existing_unresolved_backup_blocks_import_without_deletion()
    {
        var (staging, target) = CreateFolders();
        var backup = target + ".backup-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "precious.bin"), "keep");
        Assert.Throws<InvalidOperationException>(() =>
            ManualModelDirectorySwap.ReplaceStaged(staging, target));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "weights.bin")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(backup, "precious.bin")));
    }

    [Fact]
    public void Missing_or_misplaced_staging_does_not_touch_target()
    {
        var (staging, target) = CreateFolders();
        Directory.Delete(staging, recursive: true);
        Assert.Throws<DirectoryNotFoundException>(() =>
            ManualModelDirectorySwap.ReplaceStaged(staging, target));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "weights.bin")));

        var elsewhere = Path.Combine(_root, "arbitrary");
        Directory.CreateDirectory(elsewhere);
        Assert.Throws<InvalidDataException>(() =>
            ManualModelDirectorySwap.ReplaceStaged(elsewhere, target));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "weights.bin")));
    }

    [Fact]
    public void Never_removes_unrelated_outputs_or_license_acceptances()
    {
        var (staging, target) = CreateFolders();
        var output = Path.Combine(_root, "outputs");
        var license = Path.Combine(_root, "licenses");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(license);
        File.WriteAllText(Path.Combine(output, "video.mp4"), "keep-video");
        File.WriteAllText(Path.Combine(license, "accepted.json"), "keep-license");
        ManualModelDirectorySwap.ReplaceStaged(staging, target);
        Assert.Equal("keep-video", File.ReadAllText(Path.Combine(output, "video.mp4")));
        Assert.Equal("keep-license", File.ReadAllText(Path.Combine(license, "accepted.json")));
    }

    [Fact]
    public void Symlink_parent_does_not_redirect_manual_import()
    {
        var unmanaged = Path.Combine(_root, "user-owned");
        Directory.CreateDirectory(unmanaged);
        var target = Path.Combine(unmanaged, "model");
        var stage = target + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(target, "weights.bin"), "old");
        File.WriteAllText(Path.Combine(stage, "weights.bin"), "new");
        var link = Path.Combine(_root, "redirect");

        try { Directory.CreateSymbolicLink(link, unmanaged); }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }
        catch (PlatformNotSupportedException) { return; }

        Assert.Throws<InvalidDataException>(() =>
            ManualModelDirectorySwap.ReplaceStaged(
                Path.Combine(link, Path.GetFileName(stage)),
                Path.Combine(link, "model")));

        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "weights.bin")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(stage, "weights.bin")));
        Assert.Empty(Directory.GetDirectories(unmanaged, "model.backup-*"));
    }

    private (string Staging, string Target) CreateFolders()
    {
        var target = Path.Combine(_root, "model");
        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(target, "weights.bin"), "old");
        File.WriteAllText(Path.Combine(staging, "weights.bin"), "new");
        return (staging, target);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
