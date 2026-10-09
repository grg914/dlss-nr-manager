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
    public void Manager_owned_ini_edits_refresh_integrity_hash()
    {
        var ini = Path.Combine(_root, "OptiScaler.ini");
        File.WriteAllText(
            ini,
            "[DlssNr]\nEnabled=true\nWorkingScale=1.0\n[Menu]\nShowFps=true\n");

        var manifest = new InstallManifest(
            "v-test",
            "dxgi.dll",
            "Game.exe",
            "",
            "",
            DateTimeOffset.UtcNow,
            ["OptiScaler.ini", ".dlssnr-manager-state.json"],
            null,
            new Dictionary<string, string>
            {
                ["OptiScaler.ini"] = HashService.Sha256(ini)
            });

        File.WriteAllText(
            Path.Combine(_root, ".dlssnr-manager-state.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest));

        var installer = new InstallerService();
        installer.ApplyPreset(
            _root,
            "0.75",
            enableNeuralRendering: true);

        var integrity =
            new ManagedInstallIntegrityService().Verify(_root);

        Assert.True(integrity.Healthy);
        Assert.Empty(integrity.ChangedFiles);
    }

    [Theory]
    [InlineData("OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip", "v0.7.7-pre0")]
    [InlineData("OptiScaler-NR-v1.2.3-vendored-win-x64.zip", "v1.2.3")]
    public void Manager_owned_optiscaler_asset_name_round_trips_tag(
        string assetName,
        string expectedTag)
    {
        Assert.True(
            GitHubReleaseService.TryParseOptiScalerTag(
                assetName,
                out var tag));

        Assert.Equal(expectedTag, tag);
    }

    [Theory]
    [InlineData("OptiScaler-NR-v0.7.7-pre0.zip")]
    [InlineData("OptiScaler-v0.7.7-pre0-vendored-win-x64.zip")]
    [InlineData("DlssNrManager-win-x64.zip")]
    public void Non_manager_optiscaler_asset_names_are_rejected(
        string assetName)
    {
        Assert.False(
            GitHubReleaseService.TryParseOptiScalerTag(
                assetName,
                out _));
    }

    [Theory]
    [InlineData("v0.7.7-pre0", true)]
    [InlineData("v0.7.7-rc1", true)]
    [InlineData("v0.7.7-beta2", true)]
    [InlineData("v0.7.7-dev", true)]
    [InlineData("v0.7.7", false)]
    [InlineData("v0.7.7-final", false)]
    public void Optiscaler_prerelease_detection_tracks_source_tag(
        string tag,
        bool expected)
        => Assert.Equal(
            expected,
            GitHubReleaseService.IsOptiScalerPrerelease(tag));

    [Fact]
    public void Stable_manager_release_keeps_pre0_optiscaler_build_visible()
    {
        using var json = System.Text.Json.JsonDocument.Parse(
            """
            {
              "prerelease": false,
              "assets": [
                {
                  "name": "OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip",
                  "browser_download_url": "https://github.com/grg914/dlss-nr-manager/releases/download/v3.1.1/OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip",
                  "digest": "sha256:9a8e1eb945cf9438b6f78350db1f0c8e3e4383e72db7415212dbfd6234ae6"
                }
              ]
            }
            """);

        var release =
            GitHubReleaseService.TryGetManagerOwnedOptiScaler(
                json.RootElement);

        Assert.NotNull(release);
        Assert.Equal("v0.7.7-pre0", release!.Tag);
        Assert.False(release.Prerelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:1234")]
    [InlineData("sha256:zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("sha512:9a8e1eb945cf9438b6f78350db1f0c8e3e4383e72db7415212dbfd6234ae6")]
    public void Manager_owned_optiscaler_requires_full_sha256_digest(string? digest)
    {
        using var json = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                assets = new[]
                {
                    new
                    {
                        name = "OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip",
                        browser_download_url =
                            "https://github.com/grg914/dlss-nr-manager/releases/download/runtime-seed-v1/OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip",
                        digest
                    }
                }
            }));

        Assert.Null(GitHubReleaseService.TryGetManagerOwnedOptiScaler(
            json.RootElement));
    }

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("fr-CH", "fr")]
    [InlineData("en", "en")]
    [InlineData("de-CH", "en")]
    [InlineData("", "en")]
    public void Ui_language_normalization_supports_french_and_english(
        string input,
        string expected)
        => Assert.Equal(
            expected,
            UiLocalizationService.NormalizeLanguage(input));

    [Fact]
    public void Ui_translation_switches_both_directions()
    {
        Assert.Equal(
            "Bibliothèque de jeux",
            UiLocalizationService.Translate(
                "Game library",
                "fr"));

        Assert.Equal(
            "Game library",
            UiLocalizationService.Translate(
                "Bibliothèque de jeux",
                "en"));

        Assert.Equal(
            "Games & DLSS",
            UiLocalizationService.Translate(
                "Jeux & DLSS",
                "en"));
    }

    [Theory]
    [InlineData("SPBRScandi.zip", "1bb19e99208e8826eabf7775fa06353e78a6ea449d43f12cd9d26eacf5bd9177", true)]
    [InlineData("SPBRScandi.zip", "1BB19E99208E8826EABF7775FA06353E78A6EA449D43F12CD9D26EACF5BD9177", true)]
    [InlineData("SPBRScandi.zip", null, false)]
    [InlineData("SPBRScandi.zip", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [InlineData("SPBR-22_1.zip", "1bb19e99208e8826eabf7775fa06353e78a6ea449d43f12cd9d26eacf5bd9177", false)]
    [InlineData("SPBRScandi-sources.zip", "1bb19e99208e8826eabf7775fa06353e78a6ea449d43f12cd9d26eacf5bd9177", false)]
    public void Minecraft_scandi_resourcepack_requires_exact_manager_owned_asset_and_pin(
        string name, string? digest, bool expected)
    {
        Assert.Equal(expected, MinecraftIntegrationService.IsValidatedSpbrScandiAsset(name, digest));
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
