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
    private readonly AppUpdateService _appUpdater = new();
    private readonly InstallerService _installer = new();
    private readonly DiagnosticService _diagnostics = new();
    private readonly GameArtworkService _artwork = new();
    private readonly MediaService _media = new();
    private readonly AiUpscaleService _aiUpscale = new();
    private readonly AiOriginDetectionService _aiOrigin;
    private readonly ReShadeService _reshade = new();
    private readonly ComponentUpdateService _components = new();
    private readonly PcUpdateService _pcUpdates = new();
    private readonly MinecraftIntegrationService _minecraft = new();
    private readonly MinecraftDlssPackageService _minecraftDlss = new();
    private readonly MinecraftPreflightService _minecraftPreflight = new();
    private readonly MinecraftOneClickService _minecraftOneClick;
    private readonly StreamlineRuntimeService _streamline = new();
    private readonly NvidiaDlssNrDiscoveryService _nvidiaNrDiscovery = new();
    private readonly PcCleanupService _pcCleanup = new();

    private GpuInfo _gpu = new("Unknown GPU", "Unknown", false);
    private RtxCapabilities _gpuCapabilities = GpuCapabilityService.Evaluate(new("Unknown GPU", "Unknown", false));
    private ReleaseInfo? _release;
    private IReadOnlyList<ReleaseInfo> _recentReleases = [];
    private string? _runtimePath;
    private ManagerReleaseInfo? _managerRelease;
    private DetectedGame? _selectedGame;
    private IReadOnlyList<DetectedGame> _detectedGames = [];
    private IReadOnlyList<MinecraftInstallCandidate> _minecraftInstances = [];
    private MinecraftPreflightResult? _minecraftPreflightResult;
    private string? _minecraftDlssZipPath;
    private string? _minecraftDlssNrPath;
    private IReadOnlyList<PcCleanupItem> _cleanupItems = [];
    private AiOriginDetectionResult? _lastAiOriginResult;
    private string? _lastAiOriginSource;
    private readonly ManagedInstallIntegrityService _integrity = new();
    private bool _isBusy;
    private int _stateRefreshVersion;
    private CancellationTokenSource? _mediaOperationCts;
    private CancellationTokenSource? _aiOriginCts;

    private sealed record AdvancedSettingsSnapshot(
        int FpsType,
        int FpsPosition,
        bool ShowFps,
        string TargetProcessName,
        bool LoadReShade);

    public MainWindow()
    {
        // Clean up media helpers left behind by an interrupted/older manager run.
        try { ExternalProcessTracker.KillAll(); } catch { }

        var appPreferences = AppPreferencesService.Load();
        if (appPreferences.SoftwareRendering)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        InitializeComponent();

        var scanSettings = ScanSettingsService.Load();
        ScanAllDrivesCheck.IsChecked = scanSettings.ScanAllFixedDrives;

        _minecraftOneClick = new MinecraftOneClickService(_minecraft);
        _aiOrigin = new AiOriginDetectionService(_media);

        AppVersionText.Text = $"Version v{AppIdentity.VersionString}";
        SoftwareRenderingButton.Content =
            appPreferences.SoftwareRendering
                ? "Use hardware UI rendering"
                : "Use software UI rendering";

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
            : _aiUpscale.IsInstalled
                ? "AI Upscale engine installed • model verification pending."
                : "AI Upscale engine not installed yet.";

        AiOriginStatusText.Text = _aiOrigin.IsReady
            ? "AI origin detector ready."
            : "AI origin detector not installed yet.";

        _cleanupItems = _pcCleanup.CreateDefaultItems();
        PcCleanupList.ItemsSource = _cleanupItems;

        Closed += (_, _) =>
        {
            try { _mediaOperationCts?.Cancel(); } catch { }
            try { _aiOriginCts?.Cancel(); } catch { }
            try { ExternalProcessTracker.Shutdown(); } catch { }
            _mediaOperationCts?.Dispose();
            _aiOriginCts?.Dispose();
            _aiOrigin.Dispose();
        };
    }

    private async Task InitializeAsync()
    {
        using var scope = AppLogger.Scope("MainWindow.InitializeAsync");

        _gpu = await Task.Run(() => _gpus.Detect());
        _gpuCapabilities = GpuCapabilityService.Evaluate(_gpu);
        GpuText.Text = $"{_gpu.Name}  •  {_gpu.Generation}";
        GpuCompatibilityText.Text = _gpuCapabilities.Summary;
        PresetBox.IsEnabled = _gpuCapabilities.NeuralRendering;
        AutoNvidiaRuntimeCheck.IsEnabled = _gpuCapabilities.NeuralRendering;
        SelectRuntimeButton.IsEnabled = _gpuCapabilities.NeuralRendering;
        AppLogger.Info($"GPU detected: {_gpu.Name} • {_gpu.Generation} • {_gpuCapabilities.Summary}");

        if (!_gpuCapabilities.IsSupportedRtx)
        {
            MessageBox.Show(
                (_gpuCapabilities.BlockingReason ?? "Unsupported GPU.") +
                "\n\nThe application can still be opened for diagnostics/media tools, but game DLSS installation is disabled.",
                "RTX GPU not supported",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        await Task.WhenAll(
            RefreshReleaseAsync(),
            ScanGamesAsync(forceRefresh: false),
            CheckManagerUpdateAsync(),
            RefreshMinecraftCausticaBuildAsync());

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
                AppLogger.Error(
                    "Automatic media component update check failed.",
                    ex);
                MediaStatusText.Text = $"Automatic component update check failed: {ex.Message}";
            }
        }

        // Reaching this point means the updated application completed its
        // normal startup path. Only now discard the previous executable.
        _appUpdater.CleanupSuccessfulUpdateBackup();
        AppLogger.Info("Application initialization completed successfully.");
    }

    private async Task RefreshMinecraftCausticaBuildAsync()
    {
        try
        {
            MinecraftCausticaBuildText.Text =
                "Checking tested Caustica RTX build…";

            var build = await _minecraft.GetLatestCausticaBuildLabelAsync();

            MinecraftCausticaBuildText.Text =
                build == null
                    ? "Caustica RTX build: no compatible Minecraft 26.2 release found"
                    : $"Caustica RTX build: {build}";
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                $"Caustica build check failed: {ex.Message}");

            MinecraftCausticaBuildText.Text =
                "Caustica RTX build: check unavailable";
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

            preferred ??= _detectedGames.FirstOrDefault(x => x.Confidence == "Validated")
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
            AppLogger.Error("Installed-game scan failed.", ex);
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
        _managerRelease = await _releases.GetLatestManagerReleaseInfoAsync();

        AppLogger.Info(
            _managerRelease == null
                ? "Manager update check: unavailable or no published release detected."
                : $"Manager update check: latest published {_managerRelease.Tag}.");

        var current =
            typeof(MainWindow).Assembly.GetName().Version
            ?? new Version(0, 0, 0);

        if (_managerRelease == null)
        {
            AppVersionText.Text =
                $"Version v{current.Major}.{current.Minor}.{current.Build} • update check unavailable";
            ManagerUpdateButton.Visibility = Visibility.Collapsed;
            return;
        }

        if (_managerRelease.Version <= current)
        {
            AppVersionText.Text =
                $"Version v{current.Major}.{current.Minor}.{current.Build} • latest";
            ManagerUpdateButton.Visibility = Visibility.Collapsed;
            return;
        }

        AppVersionText.Text =
            $"Version v{current.Major}.{current.Minor}.{current.Build} • update available";
        ManagerUpdateButton.Content =
            $"Download & install v{_managerRelease.Version}";
        ManagerUpdateButton.Visibility = Visibility.Visible;
    }

    private async Task RefreshReleaseAsync()
    {
        try
        {
            AvailableVersionText.Text = "Checking GitHub…";

            _recentReleases = await _releases.GetRecentAsync();
            var channelPrerelease = IsPrereleaseSelected();

            var compatible = _recentReleases
                .Where(release => channelPrerelease || !release.Prerelease)
                .ToList();

            _release = compatible.FirstOrDefault()
                       ?? await _releases.GetLatestAsync(channelPrerelease);

            var selectedTag = _release?.Tag;
            OptiScalerBuildBox.ItemsSource = compatible;
            if (_release != null)
            {
                OptiScalerBuildBox.SelectedItem =
                    compatible.FirstOrDefault(item =>
                        item.Tag.Equals(
                            selectedTag,
                            StringComparison.OrdinalIgnoreCase))
                    ?? compatible.FirstOrDefault();
            }

            AvailableVersionText.Text = _release == null
                ? "No compatible ZIP release found"
                : $"Selected: {_release.Tag} • {(_release.Prerelease ? "prerelease" : "stable")}";
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
            GameSafetyText.Text = "Select a game to run the anti-cheat risk check.";
            InstallButton.IsEnabled = false;
            UpdateButton.IsEnabled = false;
            ApplyPresetButton.IsEnabled = false;
            DiagnoseButton.IsEnabled = false;
            LogBox.Text = "";
            return;
        }

        GameSafetyAssessment safety;
        try
        {
            safety = await Task.Run(() => GameSafetyService.Assess(game));
        }
        catch (Exception ex)
        {
            if (refreshVersion == _stateRefreshVersion)
            {
                GameSafetyText.Text =
                    $"Anti-cheat risk check unavailable: {ex.Message}";
            }

            safety = new GameSafetyAssessment(
                false,
                [],
                "Anti-cheat risk check unavailable.");
        }

        if (refreshVersion != _stateRefreshVersion ||
            !string.Equals(
                game,
                GamePathBox.Text,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        GameSafetyText.Text = safety.Message;
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
        RuntimeText.Text = !_gpuCapabilities.NeuralRendering
            ? $"DLSSNR runtime: not required on {_gpu.Generation} • Neural Rendering is not supported by this GPU generation"
            : !state.RuntimePresent
                ? "DLSSNR runtime: missing • other supported DLSS features can still be installed"
                : $"DLSSNR runtime: {(state.RuntimeHashValid ? "validated installed runtime" : "present but unverified/changed")} • {state.RuntimeHash}";

        InstallButton.IsEnabled = !state.Installed && _gpuCapabilities.IsSupportedRtx;
        UpdateButton.IsEnabled = state.Installed && _gpuCapabilities.IsSupportedRtx;
        ApplyPresetButton.IsEnabled = state.Installed && _gpuCapabilities.IsSupportedRtx;
        DiagnoseButton.IsEnabled = true;
        LogBox.Text = result.Log;
    }

    private async void ScanGames_Click(object sender, RoutedEventArgs e)
    {
        await ScanGamesAsync(forceRefresh: true);
        await RefreshStateAsync();
    }

    private void OpenDiagnosticLogs_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLogger.LogDirectory);

            Process.Start(new ProcessStartInfo(
                "explorer.exe",
                $"\"{AppLogger.LogDirectory}\"")
            {
                UseShellExecute = true
            });

            AppLogger.Info(
                $"Opened diagnostic log directory: {AppLogger.LogDirectory}");
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "Unable to open diagnostic log directory.",
                ex);

            MessageBox.Show(
                $"Log path:\n{AppLogger.LogPath}\n\n{ex.Message}",
                "Diagnostic logs",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Could not launch the cleanup helper process.");
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
        var renderer = await Task.Run(() =>
            RendererDetectionService.Detect(game.TargetDirectory));

        CompatibilityText.Text =
            $"{game.Confidence} compatibility • {game.Platform} • {game.Evidence}\n{renderer.Summary}";

        SelectProxy(game.RecommendedProxy);
        TargetProcessBox.Text = "";
        LoadReShadeCheck.IsChecked = false;

        DiagnosticText.Text =
            "Run Diagnose game to verify the renderer signals, OptiScaler load state, DLSSNR runtime and loader conflicts.";

        var preferredBuild =
            GamePreferenceService.ReadOptiScalerBuild(
                game.TargetDirectory);

        if (!string.IsNullOrWhiteSpace(preferredBuild) &&
            _recentReleases.FirstOrDefault(release =>
                release.Tag.Equals(
                    preferredBuild,
                    StringComparison.OrdinalIgnoreCase)) is { } savedRelease)
        {
            OptiScalerBuildBox.SelectedItem = savedRelease;
            _release = savedRelease;
        }

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

            if (!validation.Trusted)
            {
                _runtimePath = null;
                RuntimePathText.Text =
                    "Runtime rejected • it is neither a known validated build nor a trusted x64 NVIDIA-signed runtime.\n" +
                    $"SHA-256: {validation.Hash}\nAuthenticode: {signature}";
                return;
            }

            RuntimePathText.Text =
                $"{Path.GetFileName(_runtimePath)}\n" +
                $"Version: {validation.FileVersion ?? "unknown"} • x64\n" +
                $"SHA-256: {validation.Hash}\n" +
                $"{(validation.HashValid ? "Known validated runtime hash ✓" : "Trusted NVIDIA-signed runtime ✓")}\n" +
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

        var gameDir = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return;

        _gpuCapabilities = GpuCapabilityService.Evaluate(_gpu);
        GpuCompatibilityText.Text = _gpuCapabilities.Summary;

        if (!_gpuCapabilities.IsSupportedRtx)
        {
            MessageBox.Show(
                (_gpuCapabilities.BlockingReason ?? "Unsupported GPU.") +
                "\n\nDLSS NR Manager supports GeForce RTX 20, 30, 40 and 50 Series where the selected feature exists.",
                "GPU not compatible",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        GameSafetyAssessment safety;
        try
        {
            safety = await Task.Run(() => GameSafetyService.Assess(gameDir));
            GameSafetyText.Text = safety.Message;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Game safety assessment failed.", ex);
            MessageBox.Show(
                $"The anti-cheat safety check could not be completed.\n\n{ex.Message}\n\nInstallation has been cancelled.",
                "Safety check failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (safety.AntiCheatDetected)
        {
            MessageBox.Show(
                "Installation has been blocked because known anti-cheat files were detected.\n\n" +
                safety.Message +
                "\n\nDo not inject OptiScaler/proxy DLLs into multiplayer or anti-cheat-protected games. " +
                "Doing so can trigger anti-cheat enforcement and may result in an account ban.",
                "Anti-cheat detected — installation blocked",
                MessageBoxButton.OK,
                MessageBoxImage.Stop);
            return;
        }

        if (MessageBox.Show(
                "Use this only for offline/single-player play or where the game developer explicitly allows graphics injection/modding.\n\n" +
                "Do NOT use it in multiplayer or anti-cheat-protected games. Injected proxy DLLs can be treated as tampering and may result in a ban.\n\n" +
                "Confirm that you understand this risk and want to continue.",
                "Multiplayer / anti-cheat warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var enableNeuralRendering = _gpuCapabilities.NeuralRendering;

        if (enableNeuralRendering &&
            (string.IsNullOrWhiteSpace(_runtimePath) || !File.Exists(_runtimePath)))
        {
            if (AutoNvidiaRuntimeCheck.IsChecked == true)
            {
                try
                {
                    RuntimePathText.Text = "Checking the official NVIDIA Streamline package for a published DLSSNR runtime…";
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
                    var continueWithoutNr = MessageBox.Show(
                        $"No usable official DLSS Neural Rendering runtime could be prepared.\n\n{ex.Message}\n\n" +
                        "Continue with the other RTX 50 features (DLSS SR/RR, Frame Generation/MFG and Reflex) without Neural Rendering?",
                        "Neural Rendering unavailable",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (continueWithoutNr != MessageBoxResult.Yes)
                        return;

                    enableNeuralRendering = false;
                    _runtimePath = null;
                }
            }
            else
            {
                var continueWithoutNr = MessageBox.Show(
                    "RTX 50 supports Neural Rendering, but no DLSSNR runtime is selected.\n\n" +
                    "Continue with the other supported DLSS features without Neural Rendering?",
                    "Neural Rendering runtime not selected",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (continueWithoutNr != MessageBoxResult.Yes)
                    return;

                enableNeuralRendering = false;
            }
        }
        else if (!enableNeuralRendering)
        {
            RuntimePathText.Text =
                $"{_gpu.Generation}: Neural Rendering unavailable by NVIDIA hardware matrix. " +
                "The manager will install only the DLSS features supported by this RTX generation.";
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

        var proxy =
            (ProxyBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? "dxgi.dll";

        var currentState = await Task.Run(() =>
            _installer.Inspect(gameDir, _gpu.Generation));
        var rendererPreview = await Task.Run(() =>
            RendererDetectionService.Detect(gameDir));
        var preferredExecutable =
            RendererDetectionService.ReadPreferredExecutable(gameDir)
            ?? rendererPreview.Preferred?.Executable
            ?? InstallerService.FindMainExecutable(gameDir);

        var comparison =
            $"BEFORE → AFTER\n" +
            $"OptiScaler: {currentState.Version ?? "not installed"} → {_release.Tag}\n" +
            $"Proxy: {currentState.ProxyName ?? "none"} → {proxy}\n" +
            $"Renderer: {rendererPreview.Preferred?.Api ?? "unknown"}\n" +
            $"Executable: {(preferredExecutable == null ? "unknown" : Path.GetFileName(preferredExecutable))}\n" +
            $"NVIDIA runtime files: newer existing versions are preserved\n" +
            $"Backup/transaction journal: enabled";

        if (MessageBox.Show(
                comparison + "\n\nApply these changes?",
                "Review changes before installation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var advanced = CaptureAdvancedSettings();
            SetBusy(true);

            var backup = await _installer.InstallAsync(
                gameDir,
                enableNeuralRendering ? _runtimePath : null,
                _gpu,
                _release,
                proxy,
                GetSelectedWorkingScale(),
                _releases,
                enableNeuralRendering);

            string? resourceWarning = null;

            if (AutoNvidiaResourcesCheck.IsChecked == true)
            {
                try
                {
                    var resourceProgress = new Progress<string>(
                        message => RuntimePathText.Text = message);

                    var staged = await _streamline.StageSelectedResourcesAsync(
                        gameDir,
                        includeSuperResolution: _gpuCapabilities.SuperResolution,
                        includeFrameGeneration: _gpuCapabilities.FrameGeneration,
                        includeReflex: _gpuCapabilities.IsSupportedRtx,
                        includeNeuralRendering: enableNeuralRendering,
                        resourceProgress);

                    if (staged.Count > 0)
                    {
                        try
                        {
                            _installer.RegisterManagedFiles(
                                gameDir,
                                staged);
                        }
                        catch
                        {
                            foreach (var path in staged)
                            {
                                try { if (File.Exists(path)) File.Delete(path); } catch { }
                            }

                            throw;
                        }
                    }

                    RuntimePathText.Text =
                        staged.Count == 0
                            ? $"NVIDIA resources checked • {_gpu.Generation} supported feature set already satisfied."
                            : $"Added {staged.Count} managed official NVIDIA resource file(s) for {_gpu.Generation}.";
                }
                catch (Exception ex)
                {
                    resourceWarning =
                        "Optional NVIDIA resource staging could not be completed: " +
                        ex.Message;
                    RuntimePathText.Text = resourceWarning;
                    AppLogger.Warn(resourceWarning);
                }
            }

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
                $"Installation completed for {_gpu.Generation}.\n\n{_gpuCapabilities.Summary}\n\nBackup: {backup}" +
                (string.IsNullOrWhiteSpace(resourceWarning)
                    ? ""
                    : $"\n\nWarning: {resourceWarning}"),
                "DLSS NR Manager",
                MessageBoxButton.OK,
                string.IsNullOrWhiteSpace(resourceWarning)
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);

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

    private async void OptiScalerBuildBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (OptiScalerBuildBox.SelectedItem is not ReleaseInfo release)
            return;

        _release = release;
        AvailableVersionText.Text =
            $"Selected: {release.Tag} • {(release.Prerelease ? "prerelease" : "stable")}";

        var gameDir = GamePathBox.Text;
        if (!string.IsNullOrWhiteSpace(gameDir) && Directory.Exists(gameDir))
        {
            GamePreferenceService.WriteOptiScalerBuild(
                gameDir,
                release.Tag);

            GameHistoryService.Append(
                gameDir,
                "Build selection",
                $"Selected OptiScaler {release.Tag}.");
        }

        await Task.CompletedTask;
    }

    private async void AddScanRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Add a folder to DLSS NR Manager game scanning"
        };

        if (dialog.ShowDialog() != true)
            return;

        ScanSettingsService.AddRoot(dialog.FolderName);
        await ScanGamesAsync(forceRefresh: true);
    }

    private async void ClearScanRoots_Click(object sender, RoutedEventArgs e)
    {
        var current = ScanSettingsService.Load();
        ScanSettingsService.Save(current with { CustomRoots = [] });
        await ScanGamesAsync(forceRefresh: true);
    }

    private async void ScanAllDrivesCheck_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        var current = ScanSettingsService.Load();
        ScanSettingsService.Save(
            current with
            {
                ScanAllFixedDrives =
                    ScanAllDrivesCheck.IsChecked == true
            });

        await ScanGamesAsync(forceRefresh: true);
    }

    private void OpenSelectedGameFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        var gameDir = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { gameDir },
            UseShellExecute = true
        });
    }

    private void CopySelectedGamePath_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(GamePathBox.Text))
            Clipboard.SetText(GamePathBox.Text);
    }

    private void LaunchSelectedGame_Click(
        object sender,
        RoutedEventArgs e)
    {
        var gameDir = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return;

        var executable =
            RendererDetectionService.ReadPreferredExecutable(gameDir)
            ?? RendererDetectionService.Detect(gameDir).Preferred?.Executable
            ?? InstallerService.FindMainExecutable(gameDir);

        if (executable == null)
        {
            MessageBox.Show(
                "No game executable could be selected.",
                "Launch game",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true
        });
    }

    private void ChooseExecutable_Click(
        object sender,
        RoutedEventArgs e)
    {
        var gameDir = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return;

        var detection = RendererDetectionService.Detect(gameDir);
        if (detection.Candidates.Count == 0)
        {
            MessageBox.Show(
                "No renderer-aware executable was detected.",
                "Choose executable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose the real game executable",
            InitialDirectory = gameDir,
            Filter = "Executable files (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        RendererDetectionService.SetPreferredExecutable(
            gameDir,
            dialog.FileName);

        GameHistoryService.Append(
            gameDir,
            "Executable",
            $"Preferred executable set to {Path.GetFileName(dialog.FileName)}.");

        CompatibilityText.Text =
            RendererDetectionService.Detect(gameDir).Summary;
    }

    private void ViewGameHistory_Click(
        object sender,
        RoutedEventArgs e)
    {
        var gameDir = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return;

        var history = GameHistoryService.Read(gameDir);
        var text = history.Count == 0
            ? "No DLSS NR Manager history exists for this game yet."
            : string.Join(
                "\n",
                history.Take(60).Select(entry =>
                    $"{entry.At.LocalDateTime:g} • {entry.Action} • {entry.Summary}"));

        MessageBox.Show(
            text,
            "Game history",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void VerifyManagedFiles_Click(
        object sender,
        RoutedEventArgs e)
    {
        var gameDir = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(gameDir) ||
            !Directory.Exists(gameDir))
        {
            return;
        }

        try
        {
            VerifyManagedFilesButton.IsEnabled = false;
            StatusText.Text = "Verifying managed file integrity…";

            var result = await Task.Run(() =>
                _integrity.Verify(gameDir));

            var details = new List<string>
            {
                result.Summary
            };

            if (result.MissingFiles.Count > 0)
            {
                details.Add(
                    "Missing:\n" +
                    string.Join(
                        "\n",
                        result.MissingFiles.Take(20).Select(x => "• " + x)));
            }

            if (result.ChangedFiles.Count > 0)
            {
                details.Add(
                    "Changed:\n" +
                    string.Join(
                        "\n",
                        result.ChangedFiles.Take(20).Select(x => "• " + x)));
            }

            if (result.UnhashedFiles.Count > 0)
            {
                details.Add(
                    "Legacy/unhashed:\n" +
                    string.Join(
                        "\n",
                        result.UnhashedFiles.Take(20).Select(x => "• " + x)));
            }

            if (result.HasPendingTransaction)
            {
                details.Add(
                    "An interrupted managed transaction is present. Refreshing the game state will attempt safe recovery when a valid backup exists.");
            }

            StatusText.Text = result.Summary;

            MessageBox.Show(
                string.Join("\n\n", details),
                "Managed file integrity",
                MessageBoxButton.OK,
                result.Healthy
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Managed file integrity check failed.";
            MessageBox.Show(
                ex.Message,
                "Managed file integrity",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            VerifyManagedFilesButton.IsEnabled = true;
        }
    }

    private async void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GamePathBox.Text))
            return;

        try
        {
            var capabilities = GpuCapabilityService.Evaluate(_gpu);
            var gameDir = GamePathBox.Text;
            var state = await Task.Run(() =>
                _installer.Inspect(gameDir, _gpu.Generation));
            await Task.Run(() => _installer.ApplyPreset(
                gameDir,
                GetSelectedWorkingScale(),
                capabilities.NeuralRendering &&
                state.RuntimePresent &&
                state.RuntimeHashValid));
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

    private async void AnalyzePcCleanup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PcCleanupAnalyzeButton.IsEnabled = false;
            PcCleanupCleanButton.IsEnabled = false;
            PcCleanupStatusText.Text = "Analyzing caches…";

            var progress = new Progress<string>(
                message => PcCleanupStatusText.Text = message);

            _cleanupItems = await _pcCleanup.AnalyzeAsync(
                _cleanupItems,
                progress);

            PcCleanupList.ItemsSource = null;
            PcCleanupList.ItemsSource = _cleanupItems;

            var total = _cleanupItems.Sum(x => x.Bytes);
            var files = _cleanupItems.Sum(x => x.FileCount);
            var skipped = _cleanupItems.Sum(x => x.SkippedCount);

            PcCleanupStatusText.Text =
                $"Analysis complete • {files:N0} files • {PcCleanupService.FormatBytes(total)} reclaimable";

            PcCleanupTotalText.Text =
                $"Analyzed total: {PcCleanupService.FormatBytes(total)}" +
                (skipped > 0 ? $" • {skipped:N0} inaccessible/locked entries skipped" : "");
        }
        catch (Exception ex)
        {
            PcCleanupStatusText.Text = $"Cache analysis failed: {ex.Message}";
        }
        finally
        {
            PcCleanupAnalyzeButton.IsEnabled = true;
            PcCleanupCleanButton.IsEnabled = true;
        }
    }

    private async void CleanPcCleanup_Click(object sender, RoutedEventArgs e)
    {
        var selected = _cleanupItems.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(
                "Select at least one cache category.",
                "PC Cleanup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var knownBytes = selected.Sum(x => x.Bytes);
        var selectedNames = string.Join(
            "\n",
            selected.Select(x => $"• {x.Name}"));

        var answer = MessageBox.Show(
            "Delete the selected temporary/cache files?\n\n" +
            selectedNames +
            $"\n\nCurrently analyzed size: {PcCleanupService.FormatBytes(knownBytes)}\n\n" +
            "Games and applications may rebuild shader caches after cleanup. " +
            "Locked or inaccessible files will be skipped.",
            "Clean selected caches",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            PcCleanupAnalyzeButton.IsEnabled = false;
            PcCleanupCleanButton.IsEnabled = false;

            var progress = new Progress<string>(
                message => PcCleanupStatusText.Text = message);

            var result = await _pcCleanup.CleanAsync(
                _cleanupItems,
                progress);

            PcCleanupStatusText.Text =
                $"Cleanup complete • {result.DeletedFiles:N0} files • " +
                $"{PcCleanupService.FormatBytes(result.DeletedBytes)} removed" +
                (result.SkippedFiles > 0
                    ? $" • {result.SkippedFiles:N0} locked/inaccessible skipped"
                    : "");

            _cleanupItems = await _pcCleanup.AnalyzeAsync(
                _cleanupItems,
                progress);

            PcCleanupList.ItemsSource = null;
            PcCleanupList.ItemsSource = _cleanupItems;

            var remaining = _cleanupItems.Sum(x => x.Bytes);
            PcCleanupTotalText.Text =
                $"Remaining analyzed cache: {PcCleanupService.FormatBytes(remaining)}";
        }
        catch (Exception ex)
        {
            PcCleanupStatusText.Text = $"Cleanup failed: {ex.Message}";
            MessageBox.Show(
                ex.Message,
                "PC Cleanup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            PcCleanupAnalyzeButton.IsEnabled = true;
            PcCleanupCleanButton.IsEnabled = true;
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

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var game = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(game) || !Directory.Exists(game))
        {
            MessageBox.Show(
                "Select a valid game folder before exporting diagnostics.",
                "Support bundle",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save DLSS NR Manager support bundle",
            Filter = "ZIP archive (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"DlssNrManager-support-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            ExportDiagnosticsButton.IsEnabled = false;
            DiagnosticText.Text = "Collecting sanitized diagnostics…";

            var gpu = _gpu;
            var destination = dialog.FileName;
            var bundle = await Task.Run(() =>
            {
                var state = _installer.Inspect(game, gpu.Generation);
                return _diagnostics.CreateSupportBundle(
                    game,
                    gpu,
                    state,
                    destination);
            });

            DiagnosticText.Text =
                $"Support bundle saved.\n{bundle}\n\n" +
                "User-profile paths are sanitized and large logs are tail-limited.";

            MessageBox.Show(
                "Support bundle created successfully.",
                "DLSS NR Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DiagnosticText.Text = $"Support bundle failed: {ex.Message}";
            AppLogger.Error("Support bundle export failed.", ex);
            MessageBox.Show(
                ex.Message,
                "Support bundle failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = true;
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
            {
                var preferred = _minecraftInstances
                    .FirstOrDefault(instance =>
                        instance.HasTargetMinecraftVersion)
                    ?? _minecraftInstances.First();

                MinecraftInstanceBox.SelectedItem = preferred;

                AppLogger.Info(
                    "Minecraft scan detected: " +
                    string.Join(
                        " | ",
                        _minecraftInstances.Select(instance =>
                            $"{instance.DisplayName} @ {instance.RootDirectory}")));

                MinecraftStatusText.Text =
                    preferred.HasTargetMinecraftVersion
                        ? $"Detected {_minecraftInstances.Count} instance(s). Selected {preferred.DisplayName} automatically."
                        : $"Detected {_minecraftInstances.Count} instance(s). Minecraft {MinecraftIntegrationService.MinecraftVersion} was not found; selected {preferred.DisplayName}.";
            }
            else
            {
                MinecraftStatusText.Text =
                    "No Minecraft Java instance was detected. Use Choose folder for a custom launcher instance.";
            }
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
                $"Selected {candidate.DisplayName} • {candidate.RootDirectory} • Fabric: {(candidate.FabricDetected ? "detected" : "not detected")}";
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

    private bool RefreshMinecraftInstallState()
    {
        var instance = SelectedMinecraftInstance();
        var installed = instance != null &&
                        _minecraftOneClick.IsInstalled(instance.RootDirectory);

        MinecraftOneClickInstallButton.Content = installed
            ? "DLSS / RTX installed"
            : "Install DLSS / RTX";

        MinecraftOneClickInstallButton.IsEnabled =
            !installed &&
            (_minecraftPreflightResult?.CanInstall ?? true);

        MinecraftRestoreOriginalButton.IsEnabled = installed;
        MinecraftUpdateManagedButton.IsEnabled = installed;

        if (installed && instance != null)
        {
            MinecraftStatusText.Text =
                $"Minecraft DLSS / RTX is already installed for {instance.DisplayName}.";
        }

        return installed;
    }

    private async void MinecraftInstanceBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (SelectedMinecraftInstance() == null)
            return;

        RefreshMinecraftInstallState();
        await RunMinecraftPreflightAsync(showDialogOnFailure: false);
        RefreshMinecraftInstallState();
    }

    private async void RunMinecraftPreflight_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunMinecraftPreflightAsync(showDialogOnFailure: true);
    }

    private async Task<MinecraftPreflightResult?> RunMinecraftPreflightAsync(
        bool showDialogOnFailure)
    {
        var instance = SelectedMinecraftInstance();
        if (instance == null)
        {
            MinecraftPreflightSummaryText.Text =
                "Select a Minecraft Java instance first.";
            MinecraftPreflightDetailsText.Text =
                "No preflight has been run.";

            if (showDialogOnFailure)
            {
                MessageBox.Show(
                    "Select a Minecraft Java instance first.",
                    "Minecraft RTX preflight",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return null;
        }

        try
        {
            MinecraftPreflightButton.IsEnabled = false;
            MinecraftOneClickInstallButton.IsEnabled = false;
            MinecraftUpdateManagedButton.IsEnabled = false;
            MinecraftPreflightSummaryText.Text = "Running RTX preflight…";
            MinecraftPreflightDetailsText.Text =
                "Checking GPU, NVIDIA driver, Vulkan RT, Java, Minecraft/Fabric versions, renderer conflicts and write access.";

            AppLogger.Info(
                $"Minecraft RTX preflight started. Instance='{instance.RootDirectory}'.");

            var result = await _minecraftPreflight.RunAsync(instance);
            _minecraftPreflightResult = result;

            AppLogger.Info(
                $"Minecraft RTX preflight result: {result.Summary}. " +
                string.Join(
                    " | ",
                    result.Checks.Select(check =>
                        $"{check.Severity}:{check.Name}={check.Details}")));

            MinecraftPreflightSummaryText.Text =
                $"Preflight: {result.Summary}";

            var resourceKey = result.Status switch
            {
                MinecraftPreflightSeverity.Ready => "Accent",
                MinecraftPreflightSeverity.Warning => "Warning",
                _ => "Danger"
            };

            MinecraftPreflightSummaryText.Foreground =
                (System.Windows.Media.Brush)FindResource(resourceKey);

            MinecraftPreflightDetailsText.Text = string.Join(
                "\n",
                result.Checks.Select(check =>
                {
                    var icon = check.Severity switch
                    {
                        MinecraftPreflightSeverity.Ready => "✓",
                        MinecraftPreflightSeverity.Warning => "!",
                        _ => "×"
                    };

                    return $"{icon} {check.Name}: {check.Details}";
                }));

            var alreadyInstalled = _minecraftOneClick.IsInstalled(instance.RootDirectory);
            MinecraftOneClickInstallButton.IsEnabled = result.CanInstall && !alreadyInstalled;
            MinecraftOneClickInstallButton.Content = alreadyInstalled
                ? "DLSS / RTX installed"
                : "Install DLSS / RTX";
            MinecraftRestoreOriginalButton.IsEnabled = alreadyInstalled;
            MinecraftUpdateManagedButton.IsEnabled = result.CanInstall && alreadyInstalled;

            if (showDialogOnFailure ||
                result.Status == MinecraftPreflightSeverity.Unsupported)
            {
                var message = string.Join(
                    "\n\n",
                    result.Checks.Select(check =>
                        $"{check.Severity} — {check.Name}\n{check.Details}"));

                MessageBox.Show(
                    $"Overall status: {result.Summary}\n\n{message}",
                    "Minecraft RTX preflight",
                    MessageBoxButton.OK,
                    result.Status == MinecraftPreflightSeverity.Unsupported
                        ? MessageBoxImage.Error
                        : result.Status == MinecraftPreflightSeverity.Warning
                            ? MessageBoxImage.Warning
                            : MessageBoxImage.Information);
            }

            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                $"Minecraft RTX preflight failed for '{instance.RootDirectory}'.",
                ex);

            _minecraftPreflightResult = null;
            MinecraftPreflightSummaryText.Text = "Preflight failed";
            MinecraftPreflightSummaryText.Foreground =
                (System.Windows.Media.Brush)FindResource("Danger");
            MinecraftPreflightDetailsText.Text = ex.Message;
            MinecraftOneClickInstallButton.IsEnabled = false;

            if (showDialogOnFailure)
            {
                MessageBox.Show(
                    ex.Message,
                    "Minecraft RTX preflight failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return null;
        }
        finally
        {
            MinecraftPreflightButton.IsEnabled = true;

            RefreshMinecraftInstallState();
        }
    }

    private async void InstallMinecraftOneClick_Click(object sender, RoutedEventArgs e)
    {
        var instance = SelectedMinecraftInstance();
        if (instance == null)
        {
            MessageBox.Show(
                "Select a Minecraft Java instance first.",
                "Minecraft DLSS / RTX",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (RefreshMinecraftInstallState())
        {
            MessageBox.Show(
                "Minecraft DLSS / RTX is already installed for this instance. Use Restore original if you want to remove the managed installation first.",
                "Minecraft DLSS / RTX",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var preflight = await RunMinecraftPreflightAsync(
            showDialogOnFailure: false);

        if (preflight == null)
            return;

        if (!preflight.CanInstall)
        {
            var blockers = string.Join(
                "\n",
                preflight.Checks
                    .Where(check =>
                        check.Severity == MinecraftPreflightSeverity.Unsupported)
                    .Select(check => $"• {check.Name}: {check.Details}"));

            MessageBox.Show(
                "Installation is blocked by the RTX preflight:\n\n" + blockers,
                "Minecraft RTX unsupported",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (preflight.Status == MinecraftPreflightSeverity.Warning)
        {
            var warnings = string.Join(
                "\n",
                preflight.Checks
                    .Where(check =>
                        check.Severity == MinecraftPreflightSeverity.Warning)
                    .Select(check => $"• {check.Name}: {check.Details}"));

            if (MessageBox.Show(
                    "The RTX preflight found warnings:\n\n" +
                    warnings +
                    "\n\nThe installer can automatically fix some of these items. Continue?",
                    "Minecraft RTX preflight warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        var warning = MessageBox.Show(
            "One-click installation will:\n\n" +
            "• back up the current Minecraft instance state\n" +
            "• force Minecraft 26.2 to prefer Vulkan\n" +
            "• verify Java 25 x64 and install Eclipse Temurin 25 with WinGet automatically if needed\n" +
            "• install Fabric automatically if it is missing\n" +
            "• install/update Fabric API and Caustica RTX\n" +
            (MinecraftPerformancePackCheck.IsChecked == true
                ? "• install Lithium, FerriteCore, Krypton and Dynamic FPS from Modrinth\n"
                : "") +
            (MinecraftSpbrCheck.IsChecked == true
                ? "• install the validated SPBRScandi resource pack\n"
                : "") +
            "• temporarily move known conflicting renderer mods (Sodium, Iris, VulkanMod, Nvidium, Canvas, OptiFine/OptiFabric) into the backup\n" +
            "• add the Fabric launcher Java arguments required/recommended for the native renderer path\n\n" +
            "Caustica RTX provides path tracing, DLSS Ray Reconstruction, Frame Generation/MFG and NVIDIA Reflex. " +
            "Ray Reconstruction uses DLSS performance/quality modes and, when enabled, replaces the standalone Super Resolution reconstruction step. " +
            "A full Restore original action is created before changes. Continue?",
            "Install Minecraft DLSS / RTX",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (warning != MessageBoxResult.Yes)
            return;

        try
        {
            AppLogger.Info(
                $"Minecraft one-click install requested. Instance='{instance.RootDirectory}', " +
                $"performancePack={MinecraftPerformancePackCheck.IsChecked == true}, " +
                $"SPBRScandi={MinecraftSpbrCheck.IsChecked == true}.");

            MinecraftOneClickInstallButton.IsEnabled = false;
            MinecraftRestoreOriginalButton.IsEnabled = false;

            var progress = new Progress<string>(
                message =>
                {
                    MinecraftStatusText.Text = message;
                    AppLogger.Info($"Minecraft: {message}");
                });

            var result = await _minecraftOneClick.InstallAsync(
                instance,
                installFabricApi: true,
                allowPrereleaseCaustica: true,
                installRtxPerformancePack:
                    MinecraftPerformancePackCheck.IsChecked == true,
                installLabPbrResourcePack:
                    MinecraftSpbrCheck.IsChecked == true,
                progress);

            AppLogger.Info(
                "Minecraft one-click install completed successfully: " +
                string.Join(
                    ", ",
                    result.Setup.Components.Select(
                        component => $"{component.Component} {component.Version}")));

            MinecraftStatusText.Text =
                "Minecraft DLSS / RTX ready • " +
                string.Join(
                    " • ",
                    result.Setup.Components.Select(
                        component => $"{component.Component} {component.Version}"));

            var notes =
                string.Join("\n", result.Notes.Select(note => $"• {note}"));

            MessageBox.Show(
                "Installation completed.\n\n" +
                notes +
                "\n\nLaunch the Fabric profile. In Minecraft, open Options → Video Settings → Ray Tracing " +
                "to choose DLSS quality, Frame Generation/MFG multiplier and Reflex mode. " +
                "The manager already sets preferredGraphicsBackend to Vulkan.",
                "Minecraft DLSS / RTX ready",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ScanMinecraft_Click(sender, e);
            RefreshMinecraftInstallState();
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                $"Minecraft one-click install failed for '{instance.RootDirectory}'.",
                ex);

            MinecraftStatusText.Text =
                $"Minecraft one-click install failed: {ex.Message}";

            MessageBox.Show(
                ex.Message,
                "Minecraft DLSS / RTX install failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            RefreshMinecraftInstallState();
        }
    }

    private async void UpdateMinecraftManagedFiles_Click(
        object sender,
        RoutedEventArgs e)
    {
        var instance = SelectedMinecraftInstance();
        if (instance == null)
        {
            MessageBox.Show(
                "Select a Minecraft Java instance first.",
                "Minecraft managed update",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!_minecraftOneClick.IsInstalled(instance.RootDirectory))
        {
            MessageBox.Show(
                "No managed Minecraft DLSS / RTX installation is tracked for this instance. Use Install DLSS / RTX first.",
                "Minecraft managed update",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var preflight = await RunMinecraftPreflightAsync(
            showDialogOnFailure: false);

        if (preflight == null || !preflight.CanInstall)
            return;

        if (MessageBox.Show(
                "Check the managed Minecraft stack and refresh it to the newest compatible files?\n\n" +
                "The manager will back up the current managed state first, then refresh Fabric API, Caustica RTX, optional performance mods and SPBRScandi according to the current selections. " +
                "Downloaded GitHub assets are SHA-256 verified when GitHub publishes a digest.",
                "Check & update managed files",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            MinecraftUpdateManagedButton.IsEnabled = false;
            MinecraftOneClickInstallButton.IsEnabled = false;
            MinecraftRestoreOriginalButton.IsEnabled = false;

            var progress = new Progress<string>(
                message =>
                {
                    MinecraftStatusText.Text = message;
                    AppLogger.Info($"Minecraft update: {message}");
                });

            var result = await _minecraftOneClick.InstallAsync(
                instance,
                installFabricApi: true,
                allowPrereleaseCaustica: true,
                installRtxPerformancePack:
                    MinecraftPerformancePackCheck.IsChecked == true,
                installLabPbrResourcePack:
                    MinecraftSpbrCheck.IsChecked == true,
                progress);

            await RefreshMinecraftCausticaBuildAsync();
            await CheckManagerUpdateAsync();

            MinecraftStatusText.Text =
                "Managed Minecraft files refreshed • " +
                string.Join(
                    " • ",
                    result.Setup.Components.Select(
                        component => $"{component.Component} {component.Version}"));

            MessageBox.Show(
                "Managed Minecraft files are up to date. Existing manager-owned files were backed up before replacement.\n\n" +
                "If a newer DLSS NR Manager application version is available, the Application page now shows its Download & install button.",
                "Minecraft managed update complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ScanMinecraft_Click(sender, e);
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                $"Minecraft managed update failed for '{instance.RootDirectory}'.",
                ex);
            MinecraftStatusText.Text =
                $"Minecraft managed update failed: {ex.Message}";

            MessageBox.Show(
                ex.Message,
                "Minecraft managed update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            RefreshMinecraftInstallState();
        }
    }

    private void RestoreMinecraftOriginal_Click(object sender, RoutedEventArgs e)
    {
        var instance = SelectedMinecraftInstance();
        if (instance == null)
        {
            MessageBox.Show(
                "Select the Minecraft instance to restore first.",
                "Restore Minecraft",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            "Restore this Minecraft instance to the state saved immediately before the one-click DLSS / RTX installation?\n\n" +
            "This removes manager-installed Caustica/Fabric API files, restores the previous options and launcher profile, " +
            "restores renderer mods that were moved to the backup, removes Caustica native/runtime output and removes Fabric version folders only when they were created by the one-click installation.",
            "Restore original Minecraft",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            AppLogger.Info(
                $"Minecraft restore requested. Instance='{instance.RootDirectory}'.");

            MinecraftOneClickInstallButton.IsEnabled = false;
            MinecraftRestoreOriginalButton.IsEnabled = false;

            var progress = new Progress<string>(
                message => MinecraftStatusText.Text = message);

            _minecraftOneClick.RestoreOriginal(
                instance.RootDirectory,
                progress);

            AppLogger.Info(
                $"Minecraft restore completed successfully. Instance='{instance.RootDirectory}'.");

            MinecraftStatusText.Text =
                "Minecraft instance restored to its original pre-install state.";
            RefreshMinecraftInstallState();

            MessageBox.Show(
                "Minecraft has been restored from the one-click backup.",
                "Restore complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ScanMinecraft_Click(sender, e);
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                $"Minecraft restore failed for '{instance.RootDirectory}'.",
                ex);

            MinecraftStatusText.Text =
                $"Minecraft restore failed: {ex.Message}";

            MessageBox.Show(
                ex.Message,
                "Minecraft restore failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            MinecraftOneClickInstallButton.IsEnabled = true;
            MinecraftRestoreOriginalButton.IsEnabled = true;
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

    private async void CheckMinecraftNvidiaNr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            MinecraftCheckNvidiaNrButton.IsEnabled = false;
            MinecraftNvidiaNrStatusText.Text = "Checking official NVIDIA sources…";

            var progress = new Progress<string>(
                message => MinecraftNvidiaNrStatusText.Text = message);

            var instance = SelectedMinecraftInstance();
            IReadOnlyList<string> staged = [];

            if (instance != null)
            {
                var runtimeDirectory = Path.Combine(
                    instance.RootDirectory,
                    ".dlss-nr-manager-runtime");
                Directory.CreateDirectory(runtimeDirectory);

                staged = await _streamline.StageSelectedResourcesAsync(
                    runtimeDirectory,
                    includeSuperResolution: true,
                    includeFrameGeneration: true,
                    includeReflex: true,
                    includeNeuralRendering: true,
                    progress);

                AppLogger.Info(
                    $"NVIDIA Streamline official resource staging completed: {staged.Count} file(s) added to '{runtimeDirectory}'.");
            }

            var result = await _nvidiaNrDiscovery.CheckAsync(progress);

            var stagingSummary = instance == null
                ? "Select a Minecraft instance to download and stage NVIDIA runtime files."
                : staged.Count == 0
                    ? "NVIDIA runtime files are already up to date."
                    : $"Downloaded {staged.Count} missing official NVIDIA runtime file(s).";

            var neuralRenderingSummary = result.PublicSdkReady
                ? $"DLSS Neural Rendering: official public runtime detected ({result.StreamlineVersion})."
                : $"DLSS Neural Rendering: not publicly available from NVIDIA yet ({result.StreamlineVersion}).";

            MinecraftNvidiaNrStatusText.Text =
                $"{stagingSummary} {neuralRenderingSummary}";

            MessageBox.Show(
                stagingSummary + Environment.NewLine + Environment.NewLine +
                neuralRenderingSummary +
                (instance == null
                    ? Environment.NewLine + Environment.NewLine +
                      "No files were changed because no Minecraft instance is selected."
                    : string.Empty),
                "NVIDIA runtime files",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"NVIDIA DLSS-NR availability check failed: {ex}");
            MinecraftNvidiaNrStatusText.Text =
                "NVIDIA availability check failed. See diagnostic logs.";

            MessageBox.Show(
                ex.Message,
                "NVIDIA runtime check failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            MinecraftCheckNvidiaNrButton.IsEnabled = true;
        }
    }

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

    private void SelectAiOriginMedia_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select media for AI origin detection",
            Filter =
                "Supported media|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp;*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v|" +
                "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp|" +
                "Videos|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v|" +
                "All files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        AiOriginSourceBox.Text = dialog.FileName;
        AiOriginClearMediaButton.IsEnabled = true;
        AiOriginStatusText.Text = _aiOrigin.IsReady
            ? "Media selected • detector ready."
            : "Media selected • set up the detector before analysis.";
    }

    private void ClearAiOriginMedia_Click(object sender, RoutedEventArgs e)
    {
        AiOriginSourceBox.Clear();
        AiOriginClearMediaButton.IsEnabled = false;
        AiOriginStatusText.Text = _aiOrigin.IsReady
            ? "No media selected • detector ready."
            : "No media selected • detector not set up yet.";
    }

    private async void SetupAiOrigin_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            AiOriginSetupButton.IsEnabled = false;
            AiOriginAnalyzeButton.IsEnabled = false;

            var progress = new Progress<string>(
                message => AiOriginStatusText.Text = message);

            await _aiOrigin.SetupAsync(progress);
            AiOriginStatusText.Text =
                "AI origin detector ready • two-model ONNX ensemble installed.";
        }
        catch (Exception ex)
        {
            AiOriginStatusText.Text =
                $"AI origin detector setup failed: {ex.Message}";

            MessageBox.Show(
                ex.Message,
                "AI origin detector setup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            AiOriginSetupButton.IsEnabled = true;
            AiOriginAnalyzeButton.IsEnabled = true;
        }
    }

    private async void AnalyzeAiOrigin_Click(
        object sender,
        RoutedEventArgs e)
    {
        var source = AiOriginSourceBox.Text;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            MessageBox.Show(
                "Select an image or video first.",
                "AI origin detection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            _aiOriginCts?.Cancel();
            _aiOriginCts?.Dispose();
            _aiOriginCts = new CancellationTokenSource();

            AiOriginSetupButton.IsEnabled = false;
            AiOriginAnalyzeButton.IsEnabled = false;
            AiOriginCancelButton.IsEnabled = true;
            MediaProcessButton.IsEnabled = false;

            var progress = new Progress<string>(
                message => AiOriginStatusText.Text = message);

            var mode = AiOriginModeBox.SelectedIndex switch
            {
                0 => AiOriginAnalysisMode.Quick,
                2 => AiOriginAnalysisMode.Thorough,
                _ => AiOriginAnalysisMode.Balanced
            };

            var result = await _aiOrigin.AnalyzeAsync(
                source,
                progress,
                _aiOriginCts.Token,
                mode);

            _lastAiOriginResult = result;
            _lastAiOriginSource = source;
            AiOriginExportButton.IsEnabled = true;

            var provenance = result.ProvenanceSignals.Count == 0
                ? "No known generator/provenance marker found."
                : string.Join(Environment.NewLine, result.ProvenanceSignals.Select(x => "• " + x));

            AiOriginStatusText.Text = result.Summary;

            MessageBox.Show(
                $"{result.Verdict}\n\n" +
                $"AI ensemble score: {result.AiProbability:P1}\n" +
                $"Confidence: {result.Confidence:P1}\n" +
                $"Primary detector: {result.PrimaryModelProbability:P1}\n" +
                $"Secondary detector: {result.SecondaryModelProbability:P1}\n" +
                $"Model disagreement: {result.ModelDisagreement:P1}\n" +
                $"View consistency: {result.ViewConsistency:P1}\n" +
                $"Temporal consistency: {result.TemporalConsistency:P1}\n" +
                $"Analysis mode: {result.AnalysisMode}\n" +
                $"Frames analyzed: {result.FramesAnalyzed}\n" +
                (result.FramesAnalyzed > 1
                    ? $"Strong-AI frames: {result.FramesFlagged}/{result.FramesAnalyzed}\n"
                    : "") +
                $"Detector: {result.Model}\n\n" +
                $"Provenance / metadata:\n{provenance}\n\n" +
                result.Notes,
                "AI origin detection",
                MessageBoxButton.OK,
                result.Verdict.StartsWith("Likely AI", StringComparison.OrdinalIgnoreCase) ||
                result.Verdict.StartsWith("AI generator", StringComparison.OrdinalIgnoreCase)
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            AiOriginStatusText.Text = "AI origin analysis cancelled.";
        }
        catch (Exception ex)
        {
            AiOriginStatusText.Text =
                $"AI origin analysis failed: {ex.Message}";

            MessageBox.Show(
                ex.Message,
                "AI origin analysis failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            AiOriginSetupButton.IsEnabled = true;
            AiOriginAnalyzeButton.IsEnabled = true;
            AiOriginCancelButton.IsEnabled = false;
            MediaProcessButton.IsEnabled = true;
            _aiOriginCts?.Dispose();
            _aiOriginCts = null;
        }
    }

    private void CancelAiOrigin_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _aiOriginCts?.Cancel();
            AiOriginStatusText.Text = "Cancelling AI origin analysis…";
        }
        catch { }
    }

    private void ExportAiOriginReport_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_lastAiOriginResult == null ||
            string.IsNullOrWhiteSpace(_lastAiOriginSource))
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export AI origin analysis report",
            Filter = "JSON report (*.json)|*.json",
            FileName =
                $"ai-origin-report-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var payload = new
            {
                CreatedAt = DateTimeOffset.Now,
                SourceFile = Path.GetFileName(_lastAiOriginSource),
                SourceSha256 = HashService.Sha256(_lastAiOriginSource),
                Result = _lastAiOriginResult
            };

            AtomicFile.WriteAllText(
                dialog.FileName,
                System.Text.Json.JsonSerializer.Serialize(
                    payload,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));

            MessageBox.Show(
                "AI origin report exported successfully.",
                "AI origin detection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "AI origin report export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

        _mediaOperationCts?.Cancel();
        _mediaOperationCts?.Dispose();
        _mediaOperationCts = new CancellationTokenSource();
        var mediaCancellationToken = _mediaOperationCts.Token;

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
                    progress,
                    mediaCancellationToken);
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
                    progress,
                    mediaCancellationToken);

                MediaStatusText.Text =
                    "Step 2/2 • AI super-resolution…";

                result = await _aiUpscale.UpscaleAsync(
                    nrIntermediate,
                    CaptureAiUpscaleOptions(output),
                    _media,
                    progress,
                    mediaCancellationToken);
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
                    progress,
                    mediaCancellationToken);
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
        catch (OperationCanceledException)
        {
            MediaStatusText.Text = "Processing cancelled.";
            AiUpscaleStatusText.Text = "Processing cancelled.";
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
            _mediaOperationCts?.Dispose();
            _mediaOperationCts = null;
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
            var changed = await _components.EnsureMediaToolsLatestAsync(
                _media,
                progress,
                forceRefresh: true);
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

    private void ToggleSoftwareRendering_Click(
        object sender,
        RoutedEventArgs e)
    {
        var software =
            System.Windows.Media.RenderOptions.ProcessRenderMode !=
            System.Windows.Interop.RenderMode.SoftwareOnly;

        System.Windows.Media.RenderOptions.ProcessRenderMode =
            software
                ? System.Windows.Interop.RenderMode.SoftwareOnly
                : System.Windows.Interop.RenderMode.Default;

        AppPreferencesService.Save(
            new AppPreferences(software));

        SoftwareRenderingButton.Content = software
            ? "Use hardware UI rendering"
            : "Use software UI rendering";

        AppLogger.Info(
            software
                ? "WPF software rendering enabled for this session."
                : "WPF hardware rendering restored for this session.");
    }

    private async void ManagerCheckUpdate_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            ManagerCheckUpdateButton.IsEnabled = false;
            ManagerCheckUpdateButton.Content = "Checking for updates…";

            await CheckManagerUpdateAsync();

            var current =
                typeof(MainWindow).Assembly.GetName().Version
                ?? new Version(0, 0, 0);

            if (_managerRelease == null ||
                _managerRelease.Version <= current)
            {
                MessageBox.Show(
                    $"DLSS NR Manager v{current.Major}.{current.Minor}.{current.Build} is already up to date.",
                    "DLSS NR Manager update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ManagerUpdate_Click(sender, e);
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "Manual DLSS NR Manager update check failed.",
                ex);

            MessageBox.Show(
                ex.Message,
                "DLSS NR Manager update check failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ManagerCheckUpdateButton.IsEnabled = true;
            ManagerCheckUpdateButton.Content = "Check for app updates";
        }
    }

    private async void ManagerUpdate_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_managerRelease == null)
        {
            await CheckManagerUpdateAsync();
            if (_managerRelease == null)
            {
                MessageBox.Show(
                    "No published DLSS NR Manager update is currently available.",
                    "DLSS NR Manager update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        var current =
            typeof(MainWindow).Assembly.GetName().Version
            ?? new Version(0, 0, 0);

        if (_managerRelease.Version <= current)
        {
            MessageBox.Show(
                $"DLSS NR Manager v{current.Major}.{current.Minor}.{current.Build} is already the latest published version.",
                "DLSS NR Manager update",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ManagerUpdateButton.Visibility = Visibility.Collapsed;
            return;
        }

        if (MessageBox.Show(
                $"Download and install DLSS NR Manager v{_managerRelease.Version}?\n\n" +
                "The update is downloaded from this project's latest GitHub Release, " +
                "validated, then the app closes, replaces its executable and restarts automatically.",
                "Install DLSS NR Manager update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            ManagerUpdateButton.IsEnabled = false;
            ManagerUpdateButton.Content =
                $"Downloading v{_managerRelease.Version}…";

            var progress = new Progress<string>(
                message => ManagerUpdateButton.Content = message);

            AppLogger.Info(
                $"Application update requested: {_managerRelease.Tag}.");

            var staged = await _appUpdater.DownloadAndStageAsync(
                _managerRelease,
                progress);

            ManagerUpdateButton.Content = "Restarting to update…";

            AppLogger.Info(
                $"Application update staged successfully at '{staged}'. Restarting.");

            _appUpdater.ApplyAndRestart(
                staged,
                _managerRelease.Version);

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "Application self-update failed.",
                ex);

            ManagerUpdateButton.IsEnabled = true;
            ManagerUpdateButton.Content =
                $"Retry update v{_managerRelease.Version}";

            MessageBox.Show(
                ex.Message,
                "DLSS NR Manager update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
