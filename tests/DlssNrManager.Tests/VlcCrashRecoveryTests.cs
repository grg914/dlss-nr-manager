using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class VlcCrashRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "DlssNrManager.VlcCrashRecovery.Tests",
        Guid.NewGuid().ToString("N"));

    public VlcCrashRecoveryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Legacy_backup_from_crash_after_old_to_backup_is_restored()
    {
        var live = Path.Combine(_root, "vlc");
        var backup = live + ".backup";
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "previous.txt"), "working");

        var result = ManagedComponentRedownload.RecoverLegacyVlcBackup(live);

        Assert.Equal(1, result.Restored);
        Assert.Equal(0, result.Failed);
        Assert.Equal("working", File.ReadAllText(Path.Combine(live, "previous.txt")));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void Conflicting_live_and_legacy_backup_are_both_retained()
    {
        var live = Path.Combine(_root, "vlc");
        var backup = live + ".backup";
        Directory.CreateDirectory(live);
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(live, "new.txt"), "partial");
        File.WriteAllText(Path.Combine(backup, "previous.txt"), "working");

        var result = ManagedComponentRedownload.RecoverLegacyVlcBackup(live);

        Assert.Equal(0, result.Restored);
        Assert.Equal(1, result.Failed);
        Assert.Equal("partial", File.ReadAllText(Path.Combine(live, "new.txt")));
        Assert.Equal("working", File.ReadAllText(Path.Combine(backup, "previous.txt")));
    }

    [Fact]
    public void No_legacy_backup_does_not_change_a_live_runtime()
    {
        var live = Path.Combine(_root, "vlc");
        Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, "keep.txt"), "keep");

        var result = ManagedComponentRedownload.RecoverLegacyVlcBackup(live);

        Assert.Equal(0, result.Restored);
        Assert.Equal(0, result.Failed);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(live, "keep.txt")));
    }

    [Fact]
    public void Existing_transactional_recovery_is_reused_for_vlc()
    {
        var root = FindRepoRoot();
        var vlc = File.ReadAllText(Path.Combine(root, "Services", "VlcRuntimeService.cs"));
        var recover = File.ReadAllText(Path.Combine(root, "Services", "ManagedComponentRedownload.cs"));

        Assert.Contains("ManagedComponentRedownload.ReplaceAsync(", vlc);
        Assert.Contains("ManagedComponentRedownload.RecoverOwnedBackups([RootDirectory])", vlc);
        Assert.Contains("RecoverLegacyVlcBackup(Path.Combine(root, \"vlc\"))", recover);
        Assert.DoesNotContain("TryDeleteDirectory(backup)", vlc);
    }

    private static string FindRepoRoot()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path != null && !File.Exists(Path.Combine(path.FullName, "DlssNrManager.csproj")))
            path = path.Parent;
        return path?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
