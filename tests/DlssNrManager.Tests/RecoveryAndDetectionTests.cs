using Xunit;
using DlssNrManager.Services;
using DlssNrManager.Models;

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

    [Fact]
    public void Binary_marker_scanner_finds_marker_across_chunk_boundary()
    {
        const int chunk = 256 * 1024;
        const string marker = "D3D12CreateDevice";
        var path = Path.Combine(_root, "large.bin");
        var bytes = Enumerable.Repeat((byte)'x', chunk + 128).ToArray();
        var markerBytes = System.Text.Encoding.ASCII.GetBytes(marker);
        markerBytes.CopyTo(bytes, chunk - 5);
        File.WriteAllBytes(path, bytes);

        Assert.Equal(
            marker,
            BinaryMarkerScanner.FindFirst(path, [marker]));
    }

    [Fact]
    public void Managed_integrity_detects_changed_file()
    {
        var managed = Path.Combine(_root, "dxgi.dll");
        File.WriteAllText(managed, "original");

        var manifest = new InstallManifest(
            "v-test",
            "dxgi.dll",
            "Game.exe",
            "",
            "",
            DateTimeOffset.UtcNow,
            ["dxgi.dll", ".dlssnr-manager-state.json"],
            null,
            new Dictionary<string, string>
            {
                ["dxgi.dll"] = HashService.Sha256(managed)
            });

        File.WriteAllText(
            Path.Combine(_root, ".dlssnr-manager-state.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest));

        var service = new ManagedInstallIntegrityService();
        Assert.True(service.Verify(_root).Healthy);

        File.WriteAllText(managed, "changed");

        var changed = service.Verify(_root);
        Assert.False(changed.Healthy);
        Assert.Contains("dxgi.dll", changed.ChangedFiles);
    }

    [Fact]
    public async Task Network_retry_retries_only_transient_failures()
    {
        var attempts = 0;

        await NetworkRetry.ExecuteAsync(
            (_, _) =>
            {
                attempts++;
                if (attempts == 1)
                    throw new HttpRequestException("temporary");

                return Task.CompletedTask;
            },
            CancellationToken.None,
            attempts: 2);

        Assert.Equal(2, attempts);

        attempts = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            NetworkRetry.ExecuteAsync(
                (_, _) =>
                {
                    attempts++;
                    throw new InvalidDataException("permanent");
                },
                CancellationToken.None,
                attempts: 3));

        Assert.Equal(1, attempts);
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
