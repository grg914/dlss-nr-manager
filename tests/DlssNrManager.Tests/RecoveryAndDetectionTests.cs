using Xunit;
using DlssNrManager.Services;

namespace DlssNrManager.Tests;

public sealed class RecoveryAndDetectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DlssNrManager.Tests",
        Guid.NewGuid().ToString("N"));

    public RecoveryAndDetectionTests()
        => Directory.CreateDirectory(_root);

    [Fact]
    public void History_is_bounded_and_latest_first()
    {
        for (var i = 0; i < 205; i++)
            GameHistoryService.Append(_root, "Test", $"entry-{i}");

        var history = GameHistoryService.Read(_root);

        Assert.Equal(200, history.Count);
        Assert.Equal("entry-204", history[0].Summary);
        Assert.Equal("entry-5", history[^1].Summary);
    }

    [Fact]
    public void Transaction_journal_tracks_only_paths_under_game_root()
    {
        var backup = Path.Combine(_root, "backup");
        Directory.CreateDirectory(backup);

        var journal = FileTransactionJournal.Begin(
            _root,
            "test",
            backup);

        var inside = Path.Combine(_root, "dxgi.dll");
        journal.Track(inside);
        journal.Track(Path.Combine(
            Path.GetDirectoryName(_root)!,
            "outside.dll"));

        var pending = FileTransactionJournal.ReadPending(_root);

        Assert.NotNull(pending);
        Assert.Single(pending!.Files);
        Assert.Equal("dxgi.dll", pending.Files[0]);

        journal.Commit();
        Assert.Null(FileTransactionJournal.ReadPending(_root));
    }

    [Fact]
    public void Renderer_detection_ignores_launcher_only_folder()
    {
        File.WriteAllText(
            Path.Combine(_root, "launcher.exe"),
            "D3D12CreateDevice");

        var result = RendererDetectionService.Detect(_root);

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Renderer_detection_recognises_named_dx12_executable()
    {
        var game = Path.Combine(_root, "Game_dx12.exe");
        File.WriteAllBytes(game, new byte[128]);

        var result = RendererDetectionService.Detect(_root);

        Assert.NotNull(result.Preferred);
        Assert.Equal("DirectX 12", result.Preferred!.Api);
    }

    [Fact]
    public void Renderer_detection_uses_known_engine_module_signal()
    {
        var exe = Path.Combine(_root, "portal2.exe");
        Directory.CreateDirectory(Path.Combine(_root, "bin"));
        File.WriteAllBytes(exe, new byte[128]);
        File.WriteAllText(
            Path.Combine(_root, "bin", "shaderapidx9.dll"),
            "Direct3DCreate9");

        var result = RendererDetectionService.Detect(_root);

        Assert.NotNull(result.Preferred);
        Assert.Equal("DirectX 9", result.Preferred!.Api);
        Assert.Contains("engine module", result.Preferred.Evidence);
    }

    [Fact]
    public void Renderer_detection_reports_dxvk_as_vulkan_translation()
    {
        var exe = Path.Combine(_root, "Game_dx11.exe");
        File.WriteAllBytes(exe, new byte[128]);
        File.WriteAllText(
            Path.Combine(_root, "dxgi.dll"),
            "DXVK vkGetInstanceProcAddr");

        var result = RendererDetectionService.Detect(_root);

        Assert.NotNull(result.Preferred);
        Assert.Equal("Vulkan", result.Preferred!.Api);
        Assert.True(result.Preferred.VulkanWrapper);
        Assert.Contains("DXVK", result.Preferred.Evidence);
    }

    [Fact]
    public void Preferred_executable_must_stay_inside_game_root()
    {
        var game = Path.Combine(_root, "Game_dx11.exe");
        File.WriteAllBytes(game, new byte[128]);

        RendererDetectionService.SetPreferredExecutable(
            _root,
            game);

        Assert.Equal(
            Path.GetFullPath(game),
            RendererDetectionService.ReadPreferredExecutable(_root));

        var outside = Path.Combine(
            Path.GetDirectoryName(_root)!,
            "outside.exe");
        File.WriteAllBytes(outside, new byte[32]);

        Assert.Throws<InvalidOperationException>(() =>
            RendererDetectionService.SetPreferredExecutable(
                _root,
                outside));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch { }
    }
}
