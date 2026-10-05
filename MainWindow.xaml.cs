using System.Diagnostics;
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
    private readonly DiagnosticService _diagnostics = new();

    private GpuInfo _gpu = new("Unknown GPU", "Unknown", false);
    private ReleaseInfo? _release;
    private string? _runtimePath;
    private string? _managerUpdateUrl;
    private DetectedGame? _selectedGame;
    private IReadOnlyList<DetectedGame> _detectedGames = [];
    private bool _isBusy;
    private int _stateRefreshVersion;

    private sealed record AdvancedSettingsSnapshot(
        int FpsType,
        int FpsPosition,
        bool ShowFps,
        string TargetProcessName,
        bool LoadReShade);

    public MainWindow()
    {
        InitializeComponent();

        var version = typeof(MainWindow).Assembly.GetName().Version;
        AppVersionText.Text = version == null
            ? "Version v0.5.0"
            : $"Version v{version.Major}.{version.Minor}.{version.Build}";

        Loaded += async (_, _) =>
        {
            ResetPointerState();
            await InitializeAsync();
        };

        Activated += (_, _) => ResetPointerState();

        Closed += (_, _) =>
        {
            Application.Current.Shutdown();
            Environment.Exit(0);
        };
    }

    private async Task InitializeAsync()
    {
        _gpu = _gpus.Detect();
        GpuText.Text = $"{_gpu.Name}  •  {_gpu.Generation}";

        await Task.WhenAll(
            RefreshReleaseAsync(),
            ScanGamesAsync(forceRefresh: false),
            CheckManagerUpdateAsync());

        await RefreshStateAsync();
    }

    private async Task ScanGamesAsync(bool forceRefresh)
    {
        try
        {
            StatusText.Text = forceRefresh
                ? "Scanning Steam, Epic, GOG, Ubisoft, EA, Xbox and Battle.net…"
                : "Loading installed game library…";

            GameBox.IsEnabled = false;

            _detectedGames = await Task.Run(() => _games.DetectCompatibleGames(forceRefresh));
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
                CompatibilityText.Text =
                    "No compatible candidate was detected automatically. You can still select an executable folder manually.";
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

    private async Task CheckManagerUpdateAsync()
    {
        var latest = await _releases.GetLatestManagerReleaseAsync();
        if (latest.Version == null)
            return;

        var current = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
        if (latest.Version <= current)
            return;

        _managerUpdateUrl = latest.Url;
        ManagerUpdateButton.Content = $"Update v{latest.Version}";
        ManagerUpdateButton.Visibility = Visibility.Visible;
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

    private async Task RefreshStateAsync()
    {
        var refreshVersion = ++_stateRefreshVersion;
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
            DiagnoseButton.IsEnabled = false;
            LogBox.Text = "";
            return;
        }

        StatusText.Text = "Reading installation state…";
        InstallButton.IsEnabled = false;
        UpdateButton.IsEnabled = false;
        ApplyPresetButton.IsEnabled = false;
        DiagnoseButton.IsEnabled = false;

        var gpuGeneration = _gpu.Generation;
        (InstallState State, string Log) result;
        try
        {
            result = await Task.Run(() =>
            {
                var state = _installer.Inspect(game, gpuGeneration);
                var log = _installer.ReadLog(game);
                return (state, log);
            });
        }
        catch (Exception ex)
        {
            if (refreshVersion == _stateRefreshVersion)
            {
                StatusText.Text = "Unable to inspect selected game";
                RuntimeText.Text = ex.Message;
                DiagnoseButton.IsEnabled = true;
            }
            return;
        }

        if (refreshVersion != _stateRefreshVersion || !IsLoaded)
            return;

        var state = result.State;
        var gameName = _selectedGame?.Name ?? "Selected game";

        StatusText.Text = state.Installed
            ? $"{gameName} • Installed • proxy {state.ProxyName ?? "unknown"}"
            : $"{gameName} • Not installed";

        VersionText.Text = $"Installed OptiScaler package: {state.Version ?? "unknown"}";
        RuntimeText.Text = !state.RuntimePresent
            ? "DLSSNR runtime: missing"
            : $"DLSSNR runtime: {(state.RuntimeHashValid ? "valid hash" : "hash invalid")} • {state.RuntimeHash}";

        InstallButton.IsEnabled = !state.Installed;
        UpdateButton.IsEnabled = state.Installed;
        ApplyPresetButton.IsEnabled = state.Installed;
        DiagnoseButton.IsEnabled = true;
        LogBox.Text = result.Log;
    }

    private async void ScanGames_Click(object sender, RoutedEventArgs e)
    {
        await ScanGamesAsync(forceRefresh: true);
        await RefreshStateAsync();
    }

    private async void GameBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameBox.SelectedItem is not DetectedGame game)
            return;

        _selectedGame = game;
        GamePathBox.Text = game.TargetDirectory;
        CompatibilityText.Text =
            $"{game.Confidence} compatibility • {game.Platform} • {game.Evidence}";

        SelectProxy(game.RecommendedProxy);
        TargetProcessBox.Text = "";
        LoadReShadeCheck.IsChecked = false;

        DiagnosticText.Text =
            "Run Diagnose game to verify the renderer signals, OptiScaler load state, DLSSNR runtime and loader conflicts.";

        await RefreshStateAsync();
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

    private async void SelectGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the game root or the folder containing the real game executable"
        };

        if (dialog.ShowDialog() != true)
            return;

        StatusText.Text = "Inspecting selected folder…";
        var normalized = await Task.Run(() => GameDetectionService.Normalize(dialog.FolderName));

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
        TargetProcessBox.Text = "";
        LoadReShadeCheck.IsChecked = false;
        await RefreshStateAsync();
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
        RuntimePathText.Text = "Validating runtime…";

        try
        {
            var validation = await RuntimeValidationService.ValidateAsync(_runtimePath, _gpu.Generation);
            var signature = validation.SignatureValid
                ? $"trusted signature{(string.IsNullOrWhiteSpace(validation.Publisher) ? "" : $" • {validation.Publisher}")}"
                : "signature not trusted/available";

            RuntimePathText.Text =
                $"{Path.GetFileName(_runtimePath)}\n" +
                $"Version: {validation.FileVersion ?? "unknown"} • {(validation.Is64Bit ? "x64" : "not x64")}\n" +
                $"SHA-256: {validation.Hash}\n" +
                $"{(validation.HashValid ? "Expected runtime hash ✓" : "Runtime hash mismatch ✕")}\n" +
                $"Authenticode: {signature}";
        }
        catch (Exception ex)
        {
            _runtimePath = null;
            RuntimePathText.Text = $"Runtime validation failed: {ex.Message}";
        }
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
                $"{_selectedGame.Name} is classified as {_selectedGame.Confidence}, not upstream-validated.\n\n" +
                $"Target: {_selectedGame.TargetDirectory}\nEvidence: {_selectedGame.Evidence}\n\nContinue with installation?",
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
            var advanced = CaptureAdvancedSettings();
            SetBusy(true);

            var backup = await _installer.InstallAsync(
                GamePathBox.Text,
                _runtimePath,
                _gpu,
                _release,
                proxy,
                GetSelectedWorkingScale(),
                _releases);

            var gameDir = GamePathBox.Text;
            await Task.Run(() => ApplyAdvancedSettings(gameDir, advanced));

            MessageBox.Show(
                $"Installation completed.\nBackup: {backup}",
                "DLSS NR Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await RefreshStateAsync();
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

    private async void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GamePathBox.Text))
            return;

        try
        {
            await Task.Run(() => _installer.ApplyPreset(GamePathBox.Text, GetSelectedWorkingScale()));
            MessageBox.Show(
                "Preset applied. Restart the game if it is currently running.",
                "DLSS NR Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Preset failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ApplyAdvanced_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GamePathBox.Text))
            return;

        try
        {
            var gameDir = GamePathBox.Text;
            var advanced = CaptureAdvancedSettings();
            await Task.Run(() => ApplyAdvancedSettings(gameDir, advanced));

            MessageBox.Show(
                "OptiScaler advanced settings applied. Restart the game if it is running.",
                "DLSS NR Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Settings failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private AdvancedSettingsSnapshot CaptureAdvancedSettings()
        => new(
            FpsTypeBox.SelectedIndex < 0 ? 1 : FpsTypeBox.SelectedIndex,
            FpsPositionBox.SelectedIndex < 0 ? 0 : FpsPositionBox.SelectedIndex,
            ShowFpsCheck.IsChecked == true,
            TargetProcessBox.Text,
            LoadReShadeCheck.IsChecked == true);

    private void ApplyAdvancedSettings(string gameDir, AdvancedSettingsSnapshot settings)
    {
        _installer.ApplyAdvancedSettings(
            gameDir,
            settings.FpsType,
            settings.FpsPosition,
            settings.ShowFps,
            settings.TargetProcessName,
            settings.LoadReShade);
    }

    private async void Diagnose_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GamePathBox.Text))
            return;

        try
        {
            DiagnoseButton.IsEnabled = false;
            DiagnosticText.Text = "Running compatibility diagnostics…";

            var game = GamePathBox.Text;
            var gpu = _gpu;

            var report = await Task.Run(() =>
            {
                var state = _installer.Inspect(game, gpu.Generation);
                return _diagnostics.Diagnose(game, gpu, state);
            });

            DiagnosticText.Text =
                $"{report.Summary}\n\n" +
                string.Join("\n", report.Lines.Select(x => $"• {x}"));
        }
        catch (Exception ex)
        {
            DiagnosticText.Text = $"Diagnostic failed: {ex.Message}";
        }
        finally
        {
            DiagnoseButton.IsEnabled = true;
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(() => _installer.RestoreLatest(GamePathBox.Text));
            await RefreshStateAsync();
            MessageBox.Show("Latest backup restored.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Remove files managed by DLSS NR Manager? Backups will be preserved.",
                "Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            await Task.Run(() => _installer.Uninstall(GamePathBox.Text));
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Uninstall failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshReleaseAsync();
        await RefreshStateAsync();
    }

    private async void ReloadLog_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(GamePathBox.Text))
        {
            var game = GamePathBox.Text;
            LogBox.Text = await Task.Run(() => _installer.ReadLog(game));
        }
    }

    private async void ChannelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            await RefreshReleaseAsync();
    }

    private void ManagerUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_managerUpdateUrl))
            return;

        Process.Start(new ProcessStartInfo(_managerUpdateUrl) { UseShellExecute = true });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Maximize_Click(sender, e);
            return;
        }

        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

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
        _isBusy = busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
        System.Windows.Input.Mouse.OverrideCursor = busy ? System.Windows.Input.Cursors.Wait : null;

        if (busy)
        {
            InstallButton.IsEnabled = false;
            UpdateButton.IsEnabled = false;
            ApplyPresetButton.IsEnabled = false;
            DiagnoseButton.IsEnabled = false;
            return;
        }

        ResetPointerState();
        _ = RefreshStateAsync();
    }

    private void ResetPointerState()
    {
        if (_isBusy)
            return;

        if (System.Windows.Input.Mouse.Captured != null)
            System.Windows.Input.Mouse.Capture(null);

        System.Windows.Input.Mouse.OverrideCursor = null;
        Cursor = null;
    }
}
