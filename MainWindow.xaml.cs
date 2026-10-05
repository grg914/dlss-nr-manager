using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DlssNrManager.Models;
using DlssNrManager.Services;

namespace DlssNrManager;

public partial class MainWindow : Window
{
    private readonly GameDetectionService _games = new();
    private readonly GpuDetectionService _gpus = new();
    private readonly GitHubReleaseService _releases = new();
    private readonly InstallerService _installer = new();

    private GpuInfo _gpu = new("Unknown GPU", "Unknown", false);
    private ReleaseInfo? _release;
    private string? _runtimePath;
    private DetectedGame? _selectedGame;
    private IReadOnlyList<DetectedGame> _detectedGames = [];

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _gpu = _gpus.Detect();
        GpuText.Text = $"{_gpu.Name}  •  {_gpu.Generation}";

        await Task.WhenAll(
            RefreshReleaseAsync(),
            ScanGamesAsync());

        RefreshState();
    }

    private async Task ScanGamesAsync()
    {
        try
        {
            StatusText.Text = "Scanning Steam, Epic and GOG…";
            GameBox.IsEnabled = false;

            _detectedGames = await Task.Run(() => _games.DetectCompatibleGames());
            GameBox.ItemsSource = _detectedGames;

            var preferred = _detectedGames.FirstOrDefault(x =>
                                x.Name.Contains("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase))
                            ?? _detectedGames.FirstOrDefault(x => x.Confidence == "Validated")
                            ?? _detectedGames.FirstOrDefault(x => x.Confidence == "Probable")
                            ?? _detectedGames.FirstOrDefault();

            if (preferred != null)
                GameBox.SelectedItem = preferred;
            else
            {
                _selectedGame = null;
                GamePathBox.Text = "";
                CompatibilityText.Text = "No compatible candidate was detected automatically. You can still select an executable folder manually.";
                StatusText.Text = "No compatible game detected";
            }
        }
        catch (Exception ex)
        {
            CompatibilityText.Text = $"Game scan failed: {ex.Message}";
            StatusText.Text = "Game scan failed";
        }
        finally
        {
            GameBox.IsEnabled = true;
        }
    }

    private async Task RefreshReleaseAsync()
    {
        try
        {
            AvailableVersionText.Text = "Checking GitHub…";
            _release = await _releases.GetLatestAsync(IsPrereleaseSelected());
            AvailableVersionText.Text = _release == null
                ? "No compatible ZIP release found"
                : $"Available: {_release.Tag}";
        }
        catch (Exception ex)
        {
            AvailableVersionText.Text = $"Release check failed: {ex.Message}";
        }
    }

    private void RefreshState()
    {
        var game = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(game) || !Directory.Exists(game))
        {
            if (_detectedGames.Count == 0)
                StatusText.Text = "No compatible game detected";

            VersionText.Text = "";
            RuntimeText.Text = "";
            InstallButton.IsEnabled = false;
            UpdateButton.IsEnabled = false;
            ApplyPresetButton.IsEnabled = false;
            LogBox.Text = "";
            return;
        }

        var state = _installer.Inspect(game, _gpu.Generation);
        var gameName = _selectedGame?.Name ?? "Selected game";
        StatusText.Text = state.Installed
            ? $"{gameName} • Installed • proxy {state.ProxyName}"
            : $"{gameName} • Not installed";

        VersionText.Text = $"Installed version: {state.Version ?? "unknown"}";
        RuntimeText.Text = !state.RuntimePresent
            ? "DLSSNR runtime: missing"
            : $"DLSSNR runtime: {(state.RuntimeHashValid ? "valid" : "hash invalid")} • {state.RuntimeHash}";

        InstallButton.IsEnabled = !state.Installed;
        UpdateButton.IsEnabled = state.Installed;
        ApplyPresetButton.IsEnabled = state.Installed;
        LogBox.Text = _installer.ReadLog(game);
    }

    private async void ScanGames_Click(object sender, RoutedEventArgs e)
    {
        await ScanGamesAsync();
        RefreshState();
    }

    private void GameBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameBox.SelectedItem is not DetectedGame game)
            return;

        _selectedGame = game;
        GamePathBox.Text = game.TargetDirectory;
        CompatibilityText.Text =
            $"{game.Confidence} compatibility • {game.Platform} • {game.Evidence}";

        SelectProxy(game.RecommendedProxy);
        RefreshState();
    }

    private void SelectProxy(string proxyName)
    {
        foreach (var item in ProxyBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), proxyName, StringComparison.OrdinalIgnoreCase))
            {
                ProxyBox.SelectedItem = item;
                return;
            }
        }
    }

    private void SelectGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the game root or the folder containing the real game executable"
        };

        if (dialog.ShowDialog() != true)
            return;

        var normalized = GameDetectionService.Normalize(dialog.FolderName);
        if (normalized == null)
        {
            MessageBox.Show(
                "No usable game executable folder was found. Select the directory containing the real 64-bit game executable.",
                "DLSS NR Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _selectedGame = null;
        GameBox.SelectedItem = null;
        GamePathBox.Text = normalized;
        CompatibilityText.Text = "Manual target • compatibility has not been automatically validated.";
        SelectProxy("dxgi.dll");
        RefreshState();
    }

    private async void SelectRuntime_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select nvngx_dlssnr.dll",
            Filter = "DLSSNR runtime (nvngx_dlssnr.dll)|nvngx_dlssnr.dll|DLL files (*.dll)|*.dll"
        };

        if (dialog.ShowDialog() != true)
            return;

        _runtimePath = dialog.FileName;
        var hash = await HashService.Sha256Async(_runtimePath);
        var expected = _gpu.Generation == "RTX 50"
            ? InstallerService.Rtx50Hash
            : _gpu.Generation is "RTX 20" or "RTX 30" or "RTX 40"
                ? InstallerService.Rtx2040Hash
                : null;

        RuntimePathText.Text =
            $"{Path.GetFileName(_runtimePath)}\nSHA-256: {hash}\n" +
            $"{(expected != null && hash.Equals(expected, StringComparison.OrdinalIgnoreCase) ? "Valid runtime" : "Hash does not match detected GPU generation")}";
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_release == null)
        {
            MessageBox.Show("No release selected/available.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_runtimePath) || !File.Exists(_runtimePath))
        {
            MessageBox.Show("Select your nvngx_dlssnr.dll first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(GamePathBox.Text))
            return;

        if (_selectedGame is { Confidence: not "Validated" })
        {
            var answer = MessageBox.Show(
                $"{_selectedGame.Name} is classified as {_selectedGame.Confidence}, not upstream-validated.\n\nTarget: {_selectedGame.TargetDirectory}\nEvidence: {_selectedGame.Evidence}\n\nContinue with installation?",
                "Compatibility not fully validated",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;
        }
        else if (_selectedGame == null)
        {
            var answer = MessageBox.Show(
                "This manually selected target has not been automatically validated. Continue only if this is the folder containing the real game executable.",
                "Manual target",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;
        }

        var proxy = ((ComboBoxItem)ProxyBox.SelectedItem).Content?.ToString() ?? "dxgi.dll";

        try
        {
            SetBusy(true);
            var backup = await _installer.InstallAsync(
                GamePathBox.Text,
                _runtimePath,
                _gpu,
                _release,
                proxy,
                GetSelectedWorkingScale(),
                _releases);

            MessageBox.Show(
                $"Installation completed.\nBackup: {backup}",
                "DLSS NR Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            RefreshState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GamePathBox.Text))
            return;

        try
        {
            _installer.ApplyPreset(GamePathBox.Text, GetSelectedWorkingScale());
            MessageBox.Show(
                "Preset applied. Restart the game if it is currently running.",
                "DLSS NR Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RefreshState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Preset failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _installer.RestoreLatest(GamePathBox.Text);
            RefreshState();
            MessageBox.Show("Latest backup restored.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Remove files managed by DLSS NR Manager? Backups will be preserved.",
                "Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            _installer.Uninstall(GamePathBox.Text);
            RefreshState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Uninstall failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshReleaseAsync();
        RefreshState();
    }

    private void ReloadLog_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(GamePathBox.Text))
            LogBox.Text = _installer.ReadLog(GamePathBox.Text);
    }

    private async void ChannelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            await RefreshReleaseAsync();
    }

    private bool IsPrereleaseSelected() => ChannelBox.SelectedIndex == 1;

    private string GetSelectedWorkingScale()
        => PresetBox.SelectedIndex switch
        {
            1 => "0.75",
            2 => "0.50",
            _ => "1.0"
        };

    private void SetBusy(bool busy)
    {
        InstallButton.IsEnabled = !busy;
        UpdateButton.IsEnabled = !busy;
        ApplyPresetButton.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }
}
