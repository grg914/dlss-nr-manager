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
    private readonly GameArtworkService _artwork = new();
    private readonly MediaService _media = new();
    private readonly AiUpscaleService _aiUpscale = new();
    private readonly ReShadeService _reshade = new();
    private readonly ComponentUpdateService _components = new();
    private readonly PcUpdateService _pcUpdates = new();
    private readonly MinecraftIntegrationService _minecraft = new();
    private readonly MinecraftDlssPackageService _minecraftDlss = new();
    private readonly StreamlineRuntimeService _streamline = new();

    private GpuInfo _gpu = new("Unknown GPU", "Unknown", false);
    private ReleaseInfo? _release;
    private string? _runtimePath;
    private string? _managerUpdateUrl;
    private DetectedGame? _selectedGame;
    private IReadOnlyList<DetectedGame> _detectedGames = [];
    private IReadOnlyList<MinecraftInstallCandidate> _minecraftInstances = [];
    private string? _minecraftDlssZipPath;
    private string? _minecraftDlssNrPath;
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
            ? "Version v0.8.1"
            : $"Version v{version.Major}.{version.Minor}.{version.Build}";

        Loaded += async (_, _) =>
        {
            ResetPointerState();
            await InitializeAsync();
        };

        Activated += (_, _) => ResetPointerState();

        MediaStatusText.Text = _media.IsReady
            ? "Media engine ready."
            : "Media engine not installed yet.";

        AiUpscaleStatusText.Text = _aiUpscale.IsReady
            ? "AI Upscale engine ready."
            : "AI Upscale engine not installed yet.";

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

        if (AutoUpdateComponentsCheck.IsChecked == true)
        {
            try
            {
                var progress = new Progress<string>(message => MediaStatusText.Text = message);
                await _components.EnsureMediaToolsLatestAsync(_media, progress);
            }
            catch (Exception ex)
            {
                MediaStatusText.Text = $"Automatic component update check failed: {ex.Message}";
            }
        }
    }

    private async Task ScanGamesAsync(bool forceRefresh)
    {
        try
        {
            StatusText.Text = forceRefresh
                ? "Scanning Steam, Epic, GOG, itch.io, Ubisoft, EA, Xbox and Battle.net…"
                : "Loading installed game library…";

            GameBox.IsEnabled = false;

            _detectedGames = await Task.Run(() => _games.DetectCompatibleGames(forceRefresh));
            GameBox.ItemsSource = _detectedGames;

            var initiallySelectedPath = _selectedGame?.TargetDirectory;
            StatusText.Text = "Resolving game cover art…";

            _detectedGames = await _artwork.ResolveAsync(_detectedGames);
            GameBox.ItemsSource = _detectedGames;

            var preferred = !string.IsNullOrWhiteSpace(initiallySelectedPath)
                ? _detectedGames.FirstOrDefault(x =>
                    x.TargetDirectory.Equals(initiallySelectedPath, StringComparison.OrdinalIgnoreCase))
                : null;

            preferred ??= _detectedGames.FirstOrDefault(x =>
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

    private async void ClearArtworkCache_Click(object sender, RoutedEventArgs e)
    {
        // Detach the current item templates before clearing so WPF can release
        // image handles. The artwork service still uses generation-specific file
        // names, so a locked old cover cannot block the refresh.
        GameBox.ItemsSource = null;
        _artwork.ClearCache();

        StatusText.Text = "Cover cache cleared. Reloading artwork…";
        await ScanGamesAsync(forceRefresh: true);
        await RefreshStateAsync();
    }

    private void DeleteLocalAppData_Click(object sender, RoutedEventArgs e)
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager");

        var answer = MessageBox.Show(
            $"This will permanently delete all DLSS NR Manager local data after the app closes.\n\n{appDataPath}\n\nThis includes cached artwork, scan/cache data and locally installed app components. The application will close after you confirm. Continue?",
            "Delete local app data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            var processId = Environment.ProcessId;
            var escapedPath = appDataPath.Replace("'", "''", StringComparison.Ordinal);
            var command =
                $"$p = Get-Process -Id {processId} -ErrorAction SilentlyContinue; " +
                "if ($p) { $p.WaitForExit() }; " +
                $"Remove-Item -LiteralPath '{escapedPath}' -Recurse -Force -ErrorAction SilentlyContinue";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-WindowStyle");
            startInfo.ArgumentList.Add("Hidden");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(command);

            Process.Start(startInfo);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to schedule local app-data deletion.\n\n{ex.Message}",
                "Delete local app data",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

        if (string.IsNullOrWhiteSpace(GamePathBox.Text))
            return;

        if (string.IsNullOrWhiteSpace(_runtimePath) || !File.Exists(_runtimePath))
        {
            if (AutoNvidiaRuntimeCheck.IsChecked != true)
            {
                MessageBox.Show(
                    "Select nvngx_dlssnr.dll or enable automatic NVIDIA Streamline runtime download.");
                return;
            }

            try
            {
                RuntimePathText.Text = "Downloading official NVIDIA Streamline runtime…";
                var progress = new Progress<string>(message => RuntimePathText.Text = message);
                var runtime = await _streamline.EnsureLatestDlssNrAsync(
                    _gpu.Generation,
                    progress);

                _runtimePath = runtime.RuntimePath;
                RuntimePathText.Text =
                    $"{Path.GetFileName(runtime.RuntimePath)} • NVIDIA Streamline {runtime.Version} • official GitHub release";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to prepare the official NVIDIA runtime.\n\n{ex.Message}",
                    "NVIDIA Streamline runtime",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

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

            if (InstallReShadeAddonCheck.IsChecked == true)
            {
                try
                {
                    var executable = InstallerService.FindMainExecutable(gameDir);
                    if (executable != null)
                    {
                        var progress = new Progress<string>(message => DiagnosticText.Text = message);
                        await _reshade.LaunchAddonInstallerAsync(executable, progress);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"OptiScaler was installed, but the ReShade add-on installer could not be started.\n\n{ex.Message}",
                        "ReShade add-on",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

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

    private async void ScanPcUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PcUpdateScanButton.IsEnabled = false;
            PcUpdateActionButton.IsEnabled = false;
            PcUpdateStatusText.Text = "Scanning PC updates…";

            var progress = new Progress<string>(
                message => PcUpdateStatusText.Text = message);

            var result = await _pcUpdates.ScanAsync(
                forceRefresh: true,
                progress);

            PcUpdateList.ItemsSource = result.Items;
            PcUpdateStatusText.Text =
                $"Scanned {result.ScannedAt.LocalDateTime:g} • " +
                $"{result.ComputerManufacturer} {result.ComputerModel} • " +
                $"BIOS {result.BiosVersion} • {result.Items.Count} entries";
        }
        catch (Exception ex)
        {
            PcUpdateStatusText.Text = $"PC update scan failed: {ex.Message}";
        }
        finally
        {
            PcUpdateScanButton.IsEnabled = true;
        }
    }

    private async void WingetUpdateAll_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Update all applications currently matched by WinGet?\n\n" +
            "This runs winget upgrade --all. Third-party installers may open, request administrator rights, " +
            "or restart applications. Drivers, Windows Update, BIOS and firmware are not installed by this action.",
            "WinGet update all",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            WingetUpdateButton.IsEnabled = false;
            PcUpdateScanButton.IsEnabled = false;

            var progress = new Progress<string>(
                message => PcUpdateStatusText.Text = message);

            var result = await _pcUpdates.UpdateAllWingetAsync(progress);
            PcUpdateStatusText.Text = "WinGet update completed. Rescanning…";

            var scan = await _pcUpdates.ScanAsync(
                forceRefresh: true,
                progress);

            PcUpdateList.ItemsSource = scan.Items;
            PcUpdateStatusText.Text =
                $"WinGet update completed • rescanned {scan.ScannedAt.LocalDateTime:g} • {scan.Items.Count} entries";

            MessageBox.Show(
                result,
                "WinGet update result",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            PcUpdateStatusText.Text = $"WinGet update failed: {ex.Message}";
            MessageBox.Show(
                ex.Message,
                "WinGet update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            WingetUpdateButton.IsEnabled = true;
            PcUpdateScanButton.IsEnabled = true;
        }
    }

    private void ClearPcUpdateCache_Click(object sender, RoutedEventArgs e)
    {
        _pcUpdates.ClearCache();
        PcUpdateList.ItemsSource = null;
        PcUpdateStatusText.Text = "PC update scan cache cleared.";
        PcUpdateSelectionText.Text = "";
        PcUpdateActionButton.IsEnabled = false;
    }

    private void PcUpdateList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (PcUpdateList.SelectedItem is not PcUpdateItem item)
        {
            PcUpdateActionButton.IsEnabled = false;
            PcUpdateSelectionText.Text = "";
            return;
        }

        PcUpdateSelectionText.Text =
            $"{item.Name} • {item.ActionLabel}";
        PcUpdateActionButton.Content = item.ActionLabel;
        PcUpdateActionButton.IsEnabled =
            !string.IsNullOrWhiteSpace(item.ActionValue);
    }

    private void OpenPcUpdateAction_Click(object sender, RoutedEventArgs e)
    {
        if (PcUpdateList.SelectedItem is not PcUpdateItem item)
            return;

        try
        {
            _pcUpdates.OpenAction(item);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "PC Update Center",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

    private void ScanMinecraft_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _minecraftInstances = _minecraft.DetectInstances();
            MinecraftInstanceBox.ItemsSource = _minecraftInstances;

            if (_minecraftInstances.Count > 0)
                MinecraftInstanceBox.SelectedIndex = 0;

            MinecraftStatusText.Text = _minecraftInstances.Count == 0
                ? "No Minecraft Java instance was detected. Use Choose folder for a custom launcher instance."
                : $"Detected {_minecraftInstances.Count} Minecraft instance(s).";
        }
        catch (Exception ex)
        {
            MinecraftStatusText.Text = $"Minecraft scan failed: {ex.Message}";
        }
    }

    private void ChooseMinecraftFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the Minecraft Java instance root"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var candidate = _minecraft.CreateManualCandidate(dialog.FolderName);

            _minecraftInstances = _minecraftInstances
                .Where(x => !x.RootDirectory.Equals(
                    candidate.RootDirectory,
                    StringComparison.OrdinalIgnoreCase))
                .Append(candidate)
                .ToList();

            MinecraftInstanceBox.ItemsSource = null;
            MinecraftInstanceBox.ItemsSource = _minecraftInstances;
            MinecraftInstanceBox.SelectedItem = candidate;

            MinecraftStatusText.Text =
                $"Selected {candidate.RootDirectory} • Fabric: {(candidate.FabricDetected ? "detected" : "not detected")}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Minecraft instance",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private MinecraftInstallCandidate? SelectedMinecraftInstance()
        => MinecraftInstanceBox.SelectedItem as MinecraftInstallCandidate;

    private async void InstallMinecraftFabric_Click(object sender, RoutedEventArgs e)
    {
        var instance = SelectedMinecraftInstance();
        if (instance == null)
        {
            MessageBox.Show("Select a Minecraft instance first.");
            return;
        }

        var answer = MessageBox.Show(
            $"Install Fabric Loader {MinecraftIntegrationService.MinimumFabricLoader} " +
            $"for Minecraft {MinecraftIntegrationService.MinecraftVersion}?\n\n" +
            "The official Fabric Installer is downloaded from FabricMC's GitHub release. Restart Minecraft Launcher afterwards.",
            "Install Fabric",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            var progress = new Progress<string>(
                message => MinecraftStatusText.Text = message);

            await _minecraft.LaunchFabricInstallerAsync(
                instance.RootDirectory,
                progress);

            ScanMinecraft_Click(sender, e);
        }
        catch (Exception ex)
        {
            MinecraftStatusText.Text = $"Fabric install failed: {ex.Message}";
            MessageBox.Show(
                ex.Message,
                "Fabric install failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void InstallMinecraftRtx_Click(object sender, RoutedEventArgs e)
    {
        var instance = SelectedMinecraftInstance();
        if (instance == null)
        {
            MessageBox.Show("Select a Minecraft instance first.");
            return;
        }

        var warning = MessageBox.Show(
            "This installs or updates only the manager-controlled Fabric API and Caustica RTX JARs. " +
            "Caustica replaces the world renderer and may conflict with Sodium, Iris or another Vulkan/world-renderer replacement.\n\nContinue?",
            "Enable Minecraft RTX stack",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (warning != MessageBoxResult.Yes)
            return;

        try
        {
            var progress = new Progress<string>(
                message => MinecraftStatusText.Text = message);

            var result = await _minecraft.InstallMinecraftRtxAsync(
                instance,
                MinecraftInstallFabricApiCheck.IsChecked == true,
                MinecraftAllowPrereleaseCheck.IsChecked == true,
                progress);

            MinecraftStatusText.Text =
                "Minecraft RTX stack installed • " +
                string.Join(
                    " • ",
                    result.Components.Select(
                        x => $"{x.Component} {x.Version}"));
        }
        catch (Exception ex)
        {
            MinecraftStatusText.Text = $"Minecraft RTX install failed: {ex.Message}";
            MessageBox.Show(
                ex.Message,
                "Minecraft RTX install failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenMinecraftLauncher_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _minecraft.OpenMinecraftLauncher();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Minecraft Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RemoveMinecraftRtx_Click(object sender, RoutedEventArgs e)
    {
        var instance = SelectedMinecraftInstance();
        if (instance == null)
        {
            MessageBox.Show("Select a Minecraft instance first.");
            return;
        }

        if (MessageBox.Show(
                "Remove only Minecraft RTX files tracked by DLSS NR Manager? Backups are preserved.",
                "Remove Minecraft RTX",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            _minecraft.UninstallManagedMinecraftRtx(instance.RootDirectory);
            MinecraftStatusText.Text = "Managed Minecraft RTX files removed.";
        }
        catch (Exception ex)
        {
            MinecraftStatusText.Text = $"Minecraft RTX removal failed: {ex.Message}";
        }
    }

    private void SelectMinecraftDlssZip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select your local DLSS / Streamline package",
            Filter = "ZIP archives (*.zip)|*.zip|All files|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        _minecraftDlssZipPath = dialog.FileName;
        MinecraftDlssZipBox.Text = dialog.FileName;

        try
        {
            var inspection = _minecraftDlss.Inspect(
                dialog.FileName,
                CaptureMinecraftDlssSelection());

            MinecraftStatusText.Text = inspection.MissingRequiredFiles.Count == 0
                ? $"DLSS package ready • {inspection.PresentFiles.Count} files detected."
                : "DLSS package missing selected files: "
                  + string.Join(", ", inspection.MissingRequiredFiles);
        }
        catch (Exception ex)
        {
            MinecraftStatusText.Text = $"DLSS package inspection failed: {ex.Message}";
        }
    }

    private void SelectMinecraftDlssNr_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select nvngx_dlssnr.dll",
            Filter = "NVIDIA DLSSNR runtime (nvngx_dlssnr.dll)|nvngx_dlssnr.dll|DLL files (*.dll)|*.dll"
        };

        if (dialog.ShowDialog() != true)
            return;

        _minecraftDlssNrPath = dialog.FileName;
        MinecraftDlssNrBox.Text = dialog.FileName;
        MinecraftStatusText.Text = "DLSSNR runtime selected. It will be hash-validated before staging.";
    }

    private MinecraftDlssFeatureSelection CaptureMinecraftDlssSelection()
        => new(
            MinecraftDlssSrCheck.IsChecked == true,
            MinecraftDlssFgCheck.IsChecked == true,
            MinecraftDlssReflexCheck.IsChecked == true,
            MinecraftDlssNrCheck.IsChecked == true);

    private void StageMinecraftDlssPackage_Click(object sender, RoutedEventArgs e)
    {
        var instance = SelectedMinecraftInstance();

        if (instance == null)
        {
            MessageBox.Show("Select a Minecraft instance first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_minecraftDlssZipPath) ||
            !File.Exists(_minecraftDlssZipPath))
        {
            MessageBox.Show("Select your local DLSS package ZIP first.");
            return;
        }

        try
        {
            var installed = _minecraftDlss.StageSelectedRuntime(
                _minecraftDlssZipPath,
                instance.RootDirectory,
                CaptureMinecraftDlssSelection(),
                requireValidatedHashes: true);

            MinecraftStatusText.Text =
                $"Staged {installed.Count} validated DLSS/Streamline files in .dlss-nr-manager-runtime.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Stage Minecraft DLSS package",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StageMinecraftDlssNr_Click(object sender, RoutedEventArgs e)
    {
        var instance = SelectedMinecraftInstance();

        if (instance == null)
        {
            MessageBox.Show("Select a Minecraft instance first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_minecraftDlssNrPath) ||
            !File.Exists(_minecraftDlssNrPath))
        {
            MessageBox.Show("Select nvngx_dlssnr.dll first.");
            return;
        }

        try
        {
            var staged = _minecraftDlss.StageNeuralRenderingRuntime(
                _minecraftDlssNrPath,
                instance.RootDirectory,
                requireValidatedHash: true);

            MinecraftStatusText.Text =
                $"Validated DLSSNR runtime staged at {staged}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Stage DLSSNR runtime",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ClearMinecraftDlssRuntime_Click(object sender, RoutedEventArgs e)
    {
        var instance = SelectedMinecraftInstance();

        if (instance == null)
        {
            MessageBox.Show("Select a Minecraft instance first.");
            return;
        }

        if (MessageBox.Show(
                "Delete the staged .dlss-nr-manager-runtime directory for this Minecraft instance?",
                "Clear staged Minecraft runtime",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            _minecraftDlss.ClearStagedRuntime(instance.RootDirectory);
            MinecraftStatusText.Text = "Staged Minecraft DLSS runtime removed.";
        }
        catch (Exception ex)
        {
            MinecraftStatusText.Text = $"Unable to clear staged runtime: {ex.Message}";
        }
    }

    private void SelectMedia_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select an image or video",
            Filter =
                "Supported media|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp;*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v|" +
                "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp|" +
                "Videos|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v|" +
                "All files|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        MediaSourceBox.Text = dialog.FileName;

        if (string.IsNullOrWhiteSpace(MediaOutputBox.Text))
        {
            var parent = Path.GetDirectoryName(dialog.FileName)
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            MediaOutputBox.Text = Path.Combine(parent, "DLSS-NR");
        }

        MediaStatusText.Text = _media.IsReady
            ? "Ready to process selected media."
            : "Media selected. Set up the media engine first.";
    }

    private void SelectMediaOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select output folder"
        };

        if (dialog.ShowDialog() == true)
            MediaOutputBox.Text = dialog.FolderName;
    }

    private async void SetupMedia_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            MediaSetupButton.IsEnabled = false;
            MediaProcessButton.IsEnabled = false;
            var progress = new Progress<string>(message => MediaStatusText.Text = message);

            await _media.SetupAsync(progress);
            MediaStatusText.Text =
                "Media engine ready • video2dlssnr + FFmpeg installed in LocalAppData.";
        }
        catch (Exception ex)
        {
            MediaStatusText.Text = $"Media engine setup failed: {ex.Message}";
            MessageBox.Show(
                ex.Message,
                "Media setup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            MediaSetupButton.IsEnabled = true;
            MediaProcessButton.IsEnabled = true;
        }
    }

    private void MediaModeBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (AiUpscaleOptionsPanel == null)
            return;

        var mode = MediaModeBox.SelectedIndex;
        AiUpscaleOptionsPanel.Visibility =
            mode is 1 or 2
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (MediaScaleBox != null)
            MediaScaleBox.IsEnabled = mode != 1;

        if (MediaStyleBox != null)
            MediaStyleBox.IsEnabled = mode != 1;

        if (MediaIntensitySlider != null)
            MediaIntensitySlider.IsEnabled = mode != 1;
    }

    private async void SetupAiUpscale_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            AiUpscaleSetupButton.IsEnabled = false;

            var progress = new Progress<string>(
                message => AiUpscaleStatusText.Text = message);

            await _aiUpscale.SetupAsync(progress);

            AiUpscaleStatusText.Text =
                "AI Upscale engine ready • Real-ESRGAN NCNN Vulkan installed in LocalAppData.";
        }
        catch (Exception ex)
        {
            AiUpscaleStatusText.Text =
                $"AI Upscale setup failed: {ex.Message}";

            MessageBox.Show(
                ex.Message,
                "AI Upscale setup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            AiUpscaleSetupButton.IsEnabled = true;
        }
    }

    private AiUpscaleOptions CaptureAiUpscaleOptions(
        string outputDirectory)
    {
        var scale = AiScaleBox.SelectedIndex switch
        {
            1 => 3,
            2 => 4,
            _ => 2
        };

        var model = AiModelBox.SelectedIndex switch
        {
            1 => AiUpscaleModel.GeneralSoft,
            2 => AiUpscaleModel.AnimeIllustration,
            3 => AiUpscaleModel.AnimeVideo,
            _ => AiUpscaleModel.GeneralPhoto
        };

        var tile = AiTileBox.SelectedIndex switch
        {
            1 => 256,
            2 => 384,
            3 => 512,
            _ => 0
        };

        return new AiUpscaleOptions(
            scale,
            model,
            outputDirectory,
            AiTtaCheck.IsChecked == true,
            tile);
    }

    private async void ProcessMedia_Click(object sender, RoutedEventArgs e)
    {
        var source = MediaSourceBox.Text;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            MessageBox.Show(
                "Select an image or video first.",
                "DLSS NR Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var output = MediaOutputBox.Text;
        if (string.IsNullOrWhiteSpace(output))
        {
            output = Path.Combine(
                Path.GetDirectoryName(source)
                ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "DLSS-NR");
            MediaOutputBox.Text = output;
        }

        try
        {
            MediaSetupButton.IsEnabled = false;
            MediaProcessButton.IsEnabled = false;
            AiUpscaleSetupButton.IsEnabled = false;

            var scale = MediaScaleBox.SelectedIndex switch
            {
                1 => "2x",
                2 => "4K",
                _ => "Native"
            };

            var style = Math.Max(0, MediaStyleBox.SelectedIndex);
            var intensity = MediaIntensitySlider.Value;
            var mode = MediaModeBox.SelectedIndex;
            var progress = new Progress<string>(
                message =>
                {
                    MediaStatusText.Text = message;
                    if (mode is 1 or 2)
                        AiUpscaleStatusText.Text = message;
                });

            string result;

            if (mode == 1)
            {
                result = await _aiUpscale.UpscaleAsync(
                    source,
                    CaptureAiUpscaleOptions(output),
                    _media,
                    progress);
            }
            else if (mode == 2)
            {
                MediaStatusText.Text =
                    "Step 1/2 • Neural Rendering at native resolution…";

                var nrIntermediate = await _media.ProcessAsync(
                    source,
                    new MediaProcessOptions(
                        "Native",
                        style,
                        intensity,
                        output),
                    progress);

                MediaStatusText.Text =
                    "Step 2/2 • AI super-resolution…";

                result = await _aiUpscale.UpscaleAsync(
                    nrIntermediate,
                    CaptureAiUpscaleOptions(output),
                    _media,
                    progress);
            }
            else
            {
                result = await _media.ProcessAsync(
                    source,
                    new MediaProcessOptions(
                        scale,
                        style,
                        intensity,
                        output),
                    progress);
            }

            MediaStatusText.Text = $"Complete • {result}";
            if (mode is 1 or 2)
                AiUpscaleStatusText.Text = $"Complete • {result}";

            if (MessageBox.Show(
                    $"Processing complete.\n\n{result}\n\nOpen output folder?",
                    "DLSS NR Manager",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    ArgumentList = { $"/select,{result}" },
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MediaStatusText.Text = $"Processing failed: {ex.Message}";
            AiUpscaleStatusText.Text = $"Processing failed: {ex.Message}";

            MessageBox.Show(
                ex.Message,
                "Media processing failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            MediaSetupButton.IsEnabled = true;
            MediaProcessButton.IsEnabled = true;
            AiUpscaleSetupButton.IsEnabled = true;
        }
    }

    private void MediaIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MediaIntensityText != null)
            MediaIntensityText.Text = e.NewValue.ToString("0.00");
    }

    private async void InstallReShadeAddon_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GamePathBox.Text) || !Directory.Exists(GamePathBox.Text))
        {
            MessageBox.Show("Select a game first.");
            return;
        }

        var executable = InstallerService.FindMainExecutable(GamePathBox.Text);
        if (executable == null)
        {
            MessageBox.Show("No game executable was found in the selected target folder.");
            return;
        }

        var warning = MessageBox.Show(
            "This downloads the latest ReShade build with full add-on support and opens its official installer for the selected game.\n\nFull add-on support is unsigned and some anti-cheat protected games may reject it. Continue?",
            "Install ReShade add-on support",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (warning != MessageBoxResult.Yes)
            return;

        try
        {
            var progress = new Progress<string>(message => DiagnosticText.Text = message);
            await _reshade.LaunchAddonInstallerAsync(executable, progress);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "ReShade add-on setup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void CheckComponentUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var progress = new Progress<string>(message => MediaStatusText.Text = message);
            var changed = await _components.EnsureMediaToolsLatestAsync(_media, progress);
            await RefreshReleaseAsync();
            await CheckManagerUpdateAsync();

            if (!changed)
                MediaStatusText.Text = "GitHub media components are already up to date.";
        }
        catch (Exception ex)
        {
            MediaStatusText.Text = $"Component update failed: {ex.Message}";
        }
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
