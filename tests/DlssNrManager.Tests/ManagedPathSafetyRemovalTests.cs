using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class ManagedPathSafetyRemovalTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "DlssNrManager.SafeRemoval.Tests",
        Guid.NewGuid().ToString("N"));

    public ManagedPathSafetyRemovalTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Normal_directory_is_accepted_without_touching_contents()
    {
        var target = Path.Combine(_root, "owned");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "model.bin"), "intact");

        ManagedPathSafety.EnsureSafeForRemoval(target);

        Assert.Equal("intact", File.ReadAllText(Path.Combine(target, "model.bin")));
    }

    [Fact]
    public void Reparse_point_parent_cannot_redirect_a_remove_operation()
    {
        var outside = Path.Combine(_root, "unmanaged-user-data");
        Directory.CreateDirectory(outside);
        var model = Path.Combine(outside, "model");
        Directory.CreateDirectory(model);
        File.WriteAllText(Path.Combine(model, "precious.bin"), "keep");

        var link = Path.Combine(_root, "redirect");
        if (!TryCreateSymlink(link, outside))
            return; // Symlink privileges vary; verified on enabled CI hosts.

        Assert.Throws<IOException>(() =>
            ManagedPathSafety.EnsureSafeForRemoval(Path.Combine(link, "model")));

        Assert.Equal("keep", File.ReadAllText(Path.Combine(model, "precious.bin")));
    }

    [Fact]
    public void Direct_directory_symlink_is_refused()
    {
        var outside = Path.Combine(_root, "unmanaged-user-data");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");

        var link = Path.Combine(_root, "redirect");
        if (!TryCreateSymlink(link, outside))
            return;

        Assert.Throws<IOException>(() => ManagedPathSafety.EnsureSafeForRemoval(link));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(outside, "keep.txt")));
    }

    private static bool TryCreateSymlink(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); return true; }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
        catch (PlatformNotSupportedException) { return false; }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
