using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class InterruptedRedownloadRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "DlssNrManager.CrashRecovery.Tests",
        Guid.NewGuid().ToString("N"));

    public InterruptedRedownloadRecoveryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Restores_previously_working_component_and_removes_partial_install()
    {
        var target = Path.Combine(_root, "engine");
        var backup = BackupPath(target);
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(backup, "old.txt"), "last known good");
        File.WriteAllText(Path.Combine(target, "partial.txt"), "partial update");

        var report = ManagedComponentRedownload.RecoverOwnedBackups([target]);
        Assert.Equal(1, report.Restored);
        Assert.Equal(0, report.Failed);
        Assert.Equal("last known good", File.ReadAllText(Path.Combine(target, "old.txt")));
        Assert.False(File.Exists(Path.Combine(target, "partial.txt")));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void Restores_component_when_original_folder_is_missing_after_crash()
    {
        var target = Path.Combine(_root, "engine");
        var backup = BackupPath(target);
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "old.txt"), "working");
        var report = ManagedComponentRedownload.RecoverOwnedBackups([target]);
        Assert.Equal(1, report.Restored);
        Assert.Equal(0, report.Failed);
        Assert.True(File.Exists(Path.Combine(target, "old.txt")));
    }

    [Fact]
    public void Leaves_ambiguous_backups_intact_for_manual_review()
    {
        var target = Path.Combine(_root, "engine");
        var b1 = BackupPath(target);
        var b2 = BackupPath(target);
        Directory.CreateDirectory(b1);
        Directory.CreateDirectory(b2);
        var report = ManagedComponentRedownload.RecoverOwnedBackups([target]);
        Assert.Equal(0, report.Restored);
        Assert.Equal(1, report.Failed);
        Assert.True(Directory.Exists(b1));
        Assert.True(Directory.Exists(b2));
    }

    [Fact]
    public void Never_touches_user_files_outside_owned_component()
    {
        var target = Path.Combine(_root, "engine");
        var backup = BackupPath(target);
        var output = Path.Combine(_root, "outputs");
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "video.mp4"), "keep forever");
        var report = ManagedComponentRedownload.RecoverOwnedBackups([target]);
        Assert.Equal(1, report.Restored);
        Assert.Equal("keep forever", File.ReadAllText(Path.Combine(output, "video.mp4")));
        Assert.True(Directory.Exists(output));
    }

    [Fact]
    public void Ignores_lookalike_non_transactional_directories()
    {
        var target = Path.Combine(_root, "engine");
        var fake = target + ".dlssnr-redownload-backup-not-a-guid";
        Directory.CreateDirectory(fake);
        var report = ManagedComponentRedownload.RecoverOwnedBackups([target]);
        Assert.Equal(0, report.Restored);
        Assert.Equal(0, report.Failed);
        Assert.True(Directory.Exists(fake));
    }

    private static string BackupPath(string target) =>
        target + ".dlssnr-redownload-backup-" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
