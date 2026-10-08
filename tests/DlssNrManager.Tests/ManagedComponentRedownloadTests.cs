using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class ManagedComponentRedownloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "DlssNrManager.Redownload.Tests", Guid.NewGuid().ToString("N"));

    public ManagedComponentRedownloadTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Successful_replacement_deletes_backup_but_preserves_siblings()
    {
        var target = Path.Combine(_root, "runtime");
        var other = Path.Combine(_root, "output");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(target, "engine.txt"), "old");
        File.WriteAllText(Path.Combine(other, "keep.txt"), "user data");

        await ManagedComponentRedownload.ReplaceAsync([target], _ =>
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "engine.txt"), "new");
            return Task.CompletedTask;
        }, () => File.ReadAllText(Path.Combine(target, "engine.txt")) == "new");

        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "engine.txt")));
        Assert.Equal("user data", File.ReadAllText(Path.Combine(other, "keep.txt")));
        Assert.Empty(Directory.GetDirectories(_root, "*.dlssnr-redownload-backup-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Failed_download_restores_previous_verified_component()
    {
        var target = CreateOldRuntime();
        await Assert.ThrowsAsync<IOException>(() =>
            ManagedComponentRedownload.ReplaceAsync([target], _ =>
            {
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "partial.bin"), "bad");
                throw new IOException("Download failed");
            }, () => true));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "engine.txt")));
        Assert.False(File.Exists(Path.Combine(target, "partial.bin")));
    }

    [Fact]
    public async Task Invalid_replacement_rolls_back()
    {
        var target = CreateOldRuntime();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ManagedComponentRedownload.ReplaceAsync([target], _ =>
            {
                Directory.CreateDirectory(target);
                return Task.CompletedTask;
            }, () => false));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "engine.txt")));
    }

    [Fact]
    public async Task Cancellation_restores_old_component_and_propagates()
    {
        var target = CreateOldRuntime();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ManagedComponentRedownload.ReplaceAsync([target], token =>
            {
                Directory.CreateDirectory(target);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }, () => true, cancellation.Token));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "engine.txt")));
    }

    [Fact]
    public async Task Multiple_component_directories_rollback_together()
    {
        var processor = CreateOldRuntime();
        var tools = Path.Combine(_root, "tools");
        Directory.CreateDirectory(tools);
        File.WriteAllText(Path.Combine(tools, "ffmpeg.txt"), "old-ffmpeg");
        await Assert.ThrowsAsync<IOException>(() =>
            ManagedComponentRedownload.ReplaceAsync([processor, tools], _ =>
            {
                Directory.CreateDirectory(processor);
                Directory.CreateDirectory(tools);
                throw new IOException("Interrupted install");
            }, () => true));
        Assert.True(File.Exists(Path.Combine(processor, "engine.txt")));
        Assert.Equal("old-ffmpeg", File.ReadAllText(Path.Combine(tools, "ffmpeg.txt")));
    }

    [Fact]
    public async Task Reject_overlapping_directories_without_touching_original()
    {
        var target = CreateOldRuntime();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ManagedComponentRedownload.ReplaceAsync([_root, target], _ =>
            {
                throw new Exception("must not execute");
            }, () => true));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "engine.txt")));
    }

    private string CreateOldRuntime()
    {
        var path = Path.Combine(_root, "runtime");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "engine.txt"), "old");
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
