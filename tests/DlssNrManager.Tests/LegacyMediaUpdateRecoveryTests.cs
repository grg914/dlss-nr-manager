using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class LegacyMediaUpdateRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "DlssNrManager.MediaLegacyRecovery.Tests",
        Guid.NewGuid().ToString("N"));

    public LegacyMediaUpdateRecoveryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Restores_old_processor_and_tools_after_interrupted_automatic_update()
    {
        var backup = NewLegacyBackup();
        var oldProcessor = Path.Combine(backup, "video2dlssnr");
        var oldTools = Path.Combine(backup, "tools");
        Directory.CreateDirectory(oldProcessor);
        Directory.CreateDirectory(oldTools);
        File.WriteAllText(Path.Combine(oldProcessor, "old.exe"), "old-processor");
        File.WriteAllText(Path.Combine(oldTools, "old.exe"), "old-ffmpeg");
        var replacement = Path.Combine(_root, "video2dlssnr");
        Directory.CreateDirectory(replacement);
        File.WriteAllText(Path.Combine(replacement, "partial.exe"), "partial");

        var report = ManagedComponentRedownload.RecoverLegacyMediaUpdateBackups(_root);

        Assert.Equal(2, report.Restored);
        Assert.Equal(0, report.Failed);
        Assert.True(File.Exists(Path.Combine(replacement, "old.exe")));
        Assert.False(File.Exists(Path.Combine(replacement, "partial.exe")));
        Assert.True(File.Exists(Path.Combine(_root, "tools", "old.exe")));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void Restores_one_moved_component_without_touching_other_valid_component()
    {
        var backup = NewLegacyBackup();
        var oldProcessor = Path.Combine(backup, "video2dlssnr");
        Directory.CreateDirectory(oldProcessor);
        File.WriteAllText(Path.Combine(oldProcessor, "old.exe"), "old");
        var currentTools = Path.Combine(_root, "tools");
        Directory.CreateDirectory(currentTools);
        File.WriteAllText(Path.Combine(currentTools, "ffmpeg.exe"), "unchanged");
        var sibling = Path.Combine(_root, "realesrgan");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "model.bin"), "keep");

        var report = ManagedComponentRedownload.RecoverLegacyMediaUpdateBackups(_root);

        Assert.Equal(1, report.Restored);
        Assert.Equal(0, report.Failed);
        Assert.True(File.Exists(Path.Combine(_root, "video2dlssnr", "old.exe")));
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(currentTools, "ffmpeg.exe")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(sibling, "model.bin")));
    }

    [Fact]
    public void Ambiguous_legacy_backup_generations_are_preserved()
    {
        var old = NewLegacyBackup();
        var other = NewLegacyBackup();
        var report = ManagedComponentRedownload.RecoverLegacyMediaUpdateBackups(_root);
        Assert.Equal(0, report.Restored);
        Assert.Equal(1, report.Failed);
        Assert.True(Directory.Exists(old));
        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public void Unknown_backup_content_remains_for_manual_review()
    {
        var backup = NewLegacyBackup();
        File.WriteAllText(Path.Combine(backup, "unknown.log"), "do not remove");
        var report = ManagedComponentRedownload.RecoverLegacyMediaUpdateBackups(_root);
        Assert.Equal(0, report.Restored);
        Assert.Equal(1, report.Failed);
        Assert.True(File.Exists(Path.Combine(backup, "unknown.log")));
    }

    [Fact]
    public void Empty_old_backup_container_is_removed_without_deleting_other_files()
    {
        var backup = NewLegacyBackup();
        var outputs = Path.Combine(_root, "outputs");
        Directory.CreateDirectory(outputs);
        File.WriteAllText(Path.Combine(outputs, "video.mp4"), "original");
        var report = ManagedComponentRedownload.RecoverLegacyMediaUpdateBackups(_root);
        Assert.Equal(0, report.Restored);
        Assert.Equal(0, report.Failed);
        Assert.False(Directory.Exists(backup));
        Assert.Equal("original", File.ReadAllText(Path.Combine(outputs, "video.mp4")));
    }

    private string NewLegacyBackup()
    {
        var folder = Path.Combine(_root, "_update-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
