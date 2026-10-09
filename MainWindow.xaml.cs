using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DlssNrManager.Models;
using DlssNrManager.Dialogs;
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
    private readonly VlcVideoEnhancementService _vlcEnhancement = new();
    private readonly VlcRuntimeService _vlcRuntime = new();
    private readonly AiUpscaleService _aiUpscale = new();
    private readonly LocalAiStudioService _aiStudio = new();
    private readonly DownloadCenterService _downloadCenter;
    private readonly AiOriginDetectionService _aiOrigin;
    private readonly ReShadeService _reshade = new();
    private readonly ComponentUpdateService _components = new();
    private readonly OfficialUpstreamUpdateService _officialUpdates = new();
    private bool _checkingOfficialUpdates;
    private readonly PcUpdateService _pcUpdates = new();
    private readonly MinecraftIntegrationService _minecraft = new();
    private readonly MinecraftPreflightService _minecraftPreflight = new();
    private readonly MinecraftOneClickService _minecraftOneClick;
    private readonly GenericNvidiaRuntimeService _genericNvidiaRuntime = new();
    private readonly NvidiaDlssNrDiscoveryService _nvidiaNrDiscovery = new();
    private readonly PcCleanupService _pcCleanup = new();
    private readonly WindowsRepairService _windowsRepair = new();
    private readonly HardwareProfileService _hardwareProfiles = new();
    private HardwareSnapshot? _hardwareSnapshot;
    private bool _hardwareProfileControlsReady;
    private bool _hardwareProbeBusy;
    private bool _initialStartupComplete;
    private DateTimeOffset _lastHardwareProbe = DateTimeOffset.MinValue;

    private UiLocalizationController? _localization;
    private string _uiLanguage = "en";
    private bool _languageSelectorReady;

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
    private string? _gameDlssZipPath;
    private IReadOnlyList<PcCleanupItem> _cleanupItems = [];
    private AiOriginDetectionResult? _lastAiOriginResult;
    private string? _lastAiOriginSource;
    private readonly ManagedInstallIntegrityService _integrity = new();
    private bool _isBusy;
    private int _stateRefreshVersion;
    private CancellationTokenSource? _mediaOperationCts;
    private CancellationTokenSource? _permanentVideoCts;
    private CancellationTokenSource? _aiOriginCts;
    private CancellationTokenSource? _downloadCenterCts;
    private MediaUpdateAvailability _mediaUpdateStatus = MediaUpdateAvailability.UnknownLocalVersion;
    private bool _checkingDownloadCenterUpdates;
    private MediaUpdateAvailability _modelUpdateStatus = MediaUpdateAvailability.UnknownLocalVersion;
    private string? _modelUpdateEntryId;

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

        // Restore the user's opt-in; never silently enable startup updates.
        AutoUpdateComponentsCheck.IsChecked = appPreferences.AutoUpdateComponents;

        DownloadProgressHub.Changed += OnDownloadProgressChanged;
        LargeDownloadApprovalHub.ApprovalRequested =
            ConfirmLargeDownloadAsync;

        var scanSettings = ScanSettingsService.Load();
        ScanAllDrivesCheck.IsChecked = scanSettings.ScanAllFixedDrives;

        _minecraftOneClick = new MinecraftOneClickService(_minecraft);
        _aiOrigin = new AiOriginDetectionService(_media);
        _downloadCenter = new DownloadCenterService(
            _media,
            _aiUpscale,
            _aiStudio,
            _aiOrigin,
            _vlcRuntime);

        AppVersionText.Text = $"Version v{AppIdentity.VersionString}";
        SoftwareRenderingButton.Content =
            appPreferences.SoftwareRendering
                ? "Use hardware UI rendering"
                : "Use software UI rendering";

        _uiLanguage = UiLocalizationService.NormalizeLanguage(
            appPreferences.Language);
        LanguageSelector.SelectedIndex =
            _uiLanguage.Equals("fr", StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;

        var hardwarePreferences = HardwareProfileService.LoadPreferences();
        HardwareProfileBox.SelectedIndex = (int)hardwarePreferences.Profile;
        HardwareCompatibleCheck.IsChecked =
            hardwarePreferences.Mode == HardwareOperatingMode.Compatible;
        _hardwareProfileControlsReady = true;

        _localization = new UiLocalizationController(
            this,
            () => _uiLanguage);
        _localization.Apply();
        _languageSelectorReady = true;

        InitializeAiStudioUi();
        RefreshDownloadCenter();

        Loaded += async (_, _) =>
        {
            ResetPointerState();
            await InitializeAsync();
            _initialStartupComplete = true;
            await RefreshHardwareProfileAsync();
        };

        Activated += (_, _) =>
        {
            ResetPointerState();
            if (_initialStartupComplete && IsLoaded &&
                DateTimeOffset.UtcNow - _lastHardwareProbe >
                TimeSpan.FromMinutes(5))
                _ = RefreshHardwareProfileAsync();
        };

        MainMenuList.SelectionChanged += (_, _) =>
        {
            // This appended page never changes the historical indices of
            // Downloads (6), VLC (3), or the other production pages.
            if (_initialStartupComplete && IsLoaded &&
                MainMenuList.SelectedIndex == 13)
                _ = RefreshHardwareProfileAsync();
        };

        MediaStatusText.Text = _media.IsReady
            ? "Media engine ready."
            : "Media engine not installed • manage it from Téléchargements.";
        RefreshVlcEnhancementStatus();
        RefreshVideoEnhancementOptionStates();


        AiUpscaleStatusText.Text = _aiUpscale.IsReady
            ? "AI Upscale engine ready."
            : _aiUpscale.IsInstalled
                ? "AI Upscale engine installed • local verification pending."
                : "AI Upscale engine not installed • manage it from Téléchargements.";

        AiOriginStatusText.Text = _aiOrigin.IsReady
            ? "AI origin detector ready."
            : _aiOrigin.IsInstalled
                ? "AI origin detector installed • local verification pending."
                : "AI origin detector not installed • manage it from Téléchargements.";

        _cleanupItems = _pcCleanup.CreateDefaultItems();
        PcCleanupList.ItemsSource = _cleanupItems;

        Closed += (_, _) =>
        {
            DownloadProgressHub.Changed -= OnDownloadProgressChanged;
            LargeDownloadApprovalHub.ApprovalRequested = null;
            try { _mediaOperationCts?.Cancel(); } catch { }
            try { _permanentVideoCts?.Cancel(); } catch { }
            try { _aiOriginCts?.Cancel(); } catch { }
            try { _downloadCenterCts?.Cancel(); } catch { }
            try { ExternalProcessTracker.Shutdown(); } catch { }
            _mediaOperationCts?.Dispose();
            _permanentVideoCts?.Dispose();
            _aiOriginCts?.Dispose();
            _downloadCenterCts?.Dispose();
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
        GameDlssSrCheck.IsChecked = _gpuCapabilities.SuperResolution;
        GameDlssFgCheck.IsChecked = _gpuCapabilities.FrameGeneration;
        GameDlssReflexCheck.IsChecked = _gpuCapabilities.IsSupportedRtx;
        GameDlssNrCheck.IsChecked = _gpuCapabilities.NeuralRendering;
        GameDlssSrCheck.IsEnabled = _gpuCapabilities.SuperResolution;
        GameDlssFgCheck.IsEnabled = _gpuCapabilities.FrameGeneration;
        GameDlssNrCheck.IsEnabled = _gpuCapabilities.NeuralRendering;
        AppLogger.Info($"GPU detected: {_gpu.Name} • {_gpu.Generation} • {_gpuCapabilities.Summary}");

        RefreshVlcEnhancementStatus();

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
        GameDlssNrBox.Text = dialog.FileName;
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
                GameDlssNrBox.Text = "No local nvngx_dlssnr.dll selected";
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
            GameDlssNrBox.Text = "No local nvngx_dlssnr.dll selected";
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
                    RuntimePathText.Text = "Checking the manager-owned Streamline bundle for a validated DLSSNR runtime…";
                    var progress = new Progress<string>(message => RuntimePathText.Text = message);
                    var runtime = await _genericNvidiaRuntime.EnsureLatestNeuralRuntimeAsync(
                        _gpu.Generation,
                        progress);

                    _runtimePath = runtime.RuntimePath;
                    RuntimePathText.Text =
                        $"{Path.GetFileName(runtime.RuntimePath)} • DLSS NR Manager {runtime.Version} • NVIDIA-signed runtime";
                }
                catch (Exception ex)
                {
                    var continueWithoutNr = MessageBox.Show(
                        $"No usable manager-owned NVIDIA-signed DLSS Neural Rendering runtime could be prepared.\n\n{ex.Message}\n\n" +
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

                    var staged = await _genericNvidiaRuntime.StageManagerOwnedAsync(
                        gameDir,
                        new GenericNvidiaFeatureSelection(
                            _gpuCapabilities.SuperResolution,
                            _gpuCapabilities.FrameGeneration,
                            _gpuCapabilities.IsSupportedRtx,
                            enableNeuralRendering),
                        resourceProgress,
                        trackForManualCleanup: false);

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
                            : $"Added {staged.Count} manager-owned NVIDIA resource file(s) for {_gpu.Generation}.";
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

    private async void RunWindowsRepair_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            L(
                "Run Windows system repair now?\n\n" +
                "This requests administrator rights and runs DISM /Online /Cleanup-Image /RestoreHealth, " +
                "then SFC /scannow. It can take several minutes and DISM may use Windows Update to obtain repair files.\n\n" +
                "Personal files are not deleted.",
                "Lancer la réparation système Windows maintenant ?\n\n" +
                "Cette action demande les droits administrateur et exécute DISM /Online /Cleanup-Image /RestoreHealth, " +
                "puis SFC /scannow. Cela peut prendre plusieurs minutes et DISM peut utiliser Windows Update pour obtenir des fichiers de réparation.\n\n" +
                "Les fichiers personnels ne sont pas supprimés."),
            L("Windows system repair", "Réparation système Windows"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            WindowsRepairButton.IsEnabled = false;
            WindowsRepairStatusText.Text =
                L(
                    "Waiting for administrator approval…",
                    "En attente de l’autorisation administrateur…");

            AppLogger.Info("Windows DISM + SFC repair requested from PC Cleanup.");

            var result = await _windowsRepair.RunDismAndSfcAsync();

            if (result.Cancelled)
            {
                WindowsRepairStatusText.Text =
                    L(
                        "Windows repair cancelled before elevation.",
                        "Réparation Windows annulée avant l’élévation.");
                AppLogger.Info("Windows DISM + SFC repair cancelled at UAC.");
                return;
            }

            if (result.Success)
            {
                WindowsRepairStatusText.Text =
                    L(
                        "DISM + SFC completed successfully. Restart Windows if the repair window requested it.",
                        "DISM + SFC terminés avec succès. Redémarrez Windows si la fenêtre de réparation l’a demandé.");
                AppLogger.Info("Windows DISM + SFC repair completed successfully.");
                return;
            }

            WindowsRepairStatusText.Text =
                L(
                    $"DISM + SFC finished with an error (exit code {result.ExitCode}).",
                    $"DISM + SFC terminés avec une erreur (code de sortie {result.ExitCode}).");

            AppLogger.Warn(
                $"Windows DISM + SFC repair reported exit code {result.ExitCode}.");

            MessageBox.Show(
                L(
                    "One or more Windows repair tools reported an error. Run the repair again or review Windows servicing/CBS logs for details.",
                    "Un ou plusieurs outils de réparation Windows ont signalé une erreur. Relancez la réparation ou consultez les journaux de maintenance Windows/CBS pour plus de détails."),
                L("Windows system repair", "Réparation système Windows"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            WindowsRepairStatusText.Text =
                L(
                    $"Windows repair failed: {ex.Message}",
                    $"Échec de la réparation Windows : {ex.Message}");

            AppLogger.Error("Windows DISM + SFC repair failed.", ex);

            MessageBox.Show(
                ex.Message,
                L("Windows system repair", "Réparation système Windows"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            WindowsRepairButton.IsEnabled = true;
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

    private GenericNvidiaFeatureSelection CaptureGameNvidiaSelection()
        => new(
            GameDlssSrCheck.IsChecked == true,
            GameDlssFgCheck.IsChecked == true,
            GameDlssReflexCheck.IsChecked == true,
            GameDlssNrCheck.IsChecked == true);

    private void SelectGameDlssZip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a local NVIDIA DLSS / Streamline package",
            Filter = "ZIP archives (*.zip)|*.zip|All files|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        _gameDlssZipPath = dialog.FileName;
        GameDlssZipBox.Text = dialog.FileName;

        try
        {
            var inspection = _genericNvidiaRuntime.InspectLocalPackage(
                dialog.FileName,
                CaptureGameNvidiaSelection());

            RuntimePathText.Text = inspection.MissingRequiredFiles.Count == 0
                ? $"Local package ready • {inspection.PresentFiles.Count} runtime file(s) detected. Fingerprints are checked again before staging."
                : "Local package is missing selected runtime files: " +
                  string.Join(", ", inspection.MissingRequiredFiles);
        }
        catch (Exception ex)
        {
            RuntimePathText.Text = $"Local package inspection failed: {ex.Message}";
        }
    }

    private async void CheckGameNvidiaRuntime_Click(object sender, RoutedEventArgs e)
    {
        var gameDir = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show("Select a game target first.");
            return;
        }

        try
        {
            var safety = await Task.Run(() => GameSafetyService.Assess(gameDir));
            GameSafetyText.Text = safety.Message;

            if (safety.AntiCheatDetected)
            {
                MessageBox.Show(
                    "Manager-owned NVIDIA runtime staging is blocked because known anti-cheat files were detected.",
                    "Anti-cheat detected — runtime staging blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Stop);
                return;
            }

            GameCheckNvidiaRuntimeButton.IsEnabled = false;
            RuntimePathText.Text = "Checking manager-owned NVIDIA Streamline runtime…";

            var progress = new Progress<string>(message => RuntimePathText.Text = message);
            var staged = await _genericNvidiaRuntime.StageManagerOwnedAsync(
                gameDir,
                CaptureGameNvidiaSelection(),
                progress);

            if (staged.Count > 0 && InstallerService.ReadManifest(gameDir) != null)
                _installer.RegisterManagedFiles(gameDir, staged);

            var availability = await _nvidiaNrDiscovery.CheckAsync();

            RuntimePathText.Text = staged.Count == 0
                ? $"Manager-owned runtime verified • no selected files were missing. {availability.Summary}"
                : $"Staged {staged.Count} manager-owned NVIDIA runtime file(s). {availability.Summary}";
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Generic NVIDIA runtime staging failed: {ex}");
            RuntimePathText.Text = $"NVIDIA runtime staging failed: {ex.Message}";
            MessageBox.Show(
                ex.Message,
                "NVIDIA runtime staging failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            GameCheckNvidiaRuntimeButton.IsEnabled = true;
        }
    }

    private void StageGameDlssPackage_Click(object sender, RoutedEventArgs e)
    {
        var gameDir = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show("Select a game target first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_gameDlssZipPath) ||
            !File.Exists(_gameDlssZipPath))
        {
            MessageBox.Show("Select a local DLSS / Streamline package ZIP first.");
            return;
        }

        try
        {
            var staged = _genericNvidiaRuntime.StageLocalPackage(
                _gameDlssZipPath,
                gameDir,
                CaptureGameNvidiaSelection(),
                requireValidatedHashes: true);

            if (staged.Count > 0 && InstallerService.ReadManifest(gameDir) != null)
                _installer.RegisterManagedFiles(gameDir, staged);

            RuntimePathText.Text = staged.Count == 0
                ? "Selected NVIDIA runtime files are already present; existing game DLLs were left untouched."
                : $"Staged {staged.Count} validated NVIDIA runtime file(s) from the selected package.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Stage NVIDIA runtime package",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ClearGameNvidiaRuntime_Click(object sender, RoutedEventArgs e)
    {
        var gameDir = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            _gameDlssZipPath = null;
            _runtimePath = null;
            GameDlssZipBox.Text = "No local DLSS / Streamline package selected";
            GameDlssNrBox.Text = "No local nvngx_dlssnr.dll selected";
            RuntimePathText.Text = "Local runtime selections cleared.";
            return;
        }

        if (MessageBox.Show(
                "Remove NVIDIA runtime files manually staged by the generic Jeux & DLSS runtime manager? Existing vendor files and files modified after staging are preserved. Local selections will also be cleared.",
                "Clear managed NVIDIA runtime",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var result = _genericNvidiaRuntime.ClearManagedRuntime(gameDir);
            _gameDlssZipPath = null;
            _runtimePath = null;
            GameDlssZipBox.Text = "No local DLSS / Streamline package selected";
            GameDlssNrBox.Text = "No local nvngx_dlssnr.dll selected";
            RuntimePathText.Text =
                $"Generic NVIDIA runtime cleanup complete • removed {result.RemovedFiles} file(s)" +
                (result.PreservedModifiedFiles > 0
                    ? $" • preserved {result.PreservedModifiedFiles} modified file(s)."
                    : ".");
        }
        catch (Exception ex)
        {
            RuntimePathText.Text = $"Unable to clear generic NVIDIA runtime: {ex.Message}";
        }
    }

    private void RefreshVlcEnhancementStatus()
    {
        try
        {
            var status = _vlcEnhancement.Detect();
            var gpu = _gpu.Name.Equals(
                    "Unknown GPU",
                    StringComparison.OrdinalIgnoreCase)
                ? ""
                : $" • GPU: {_gpu.Name}";

            VlcStatusText.Text = status.Summary + gpu;
            LaunchVlcEnhancedButton.IsEnabled =
                status.Found &&
                status.SupportsD3d11EnhancementOptions;
        }
        catch (Exception ex)
        {
            VlcStatusText.Text =
                L($"VLC detection failed: {ex.Message}", $"Échec de la détection VLC : {ex.Message}");
            LaunchVlcEnhancedButton.IsEnabled = false;
        }
    }

    private void SelectVlcMedia_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L("Select a local video for VLC enhancement", "Sélectionnez une vidéo locale pour l’amélioration VLC"),
            Filter =
                "Videos|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.ts;*.m2ts;*.mpeg;*.mpg|" +
                "All files|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        VlcMediaBox.Text = dialog.FileName;
        VlcLaunchStatusText.Text =
            L("Video selected • choose VSR, HDR and display mode, then launch VLC.", "Vidéo sélectionnée • choisissez VSR, HDR et le mode d’affichage, puis lancez VLC.");
    }

    private void RefreshVlc_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshVlcEnhancementStatus();
    }

    private void OpenVlcDownloadCenter_Click(
        object sender,
        RoutedEventArgs e)
    {
        const int downloadsMenuIndex = 6;
        MainMenuList.SelectedIndex = downloadsMenuIndex;
        RefreshDownloadCenter();

        if (DownloadCenterList?.ItemsSource is IEnumerable<DownloadCenterEntry> entries)
        {
            DownloadCenterList.SelectedItem =
                entries.FirstOrDefault(x => x.Id == "vlc-runtime");
        }
    }

    private void VlcRealtimeOption_Changed(
        object sender,
        RoutedEventArgs e)
    {
        RefreshVideoEnhancementOptionStates();
    }

    private void PermanentVideoOption_Changed(
        object sender,
        RoutedEventArgs e)
    {
        RefreshVideoEnhancementOptionStates();
    }

    private void RefreshVideoEnhancementOptionStates()
    {
        if (VlcScaleCheck == null ||
            VlcScaleBox == null ||
            VlcHdrModeBox == null ||
            PermanentVideoScaleBox == null ||
            PermanentVideoModelBox == null)
        {
            return;
        }

        var fullscreen =
            VlcFullscreenCheck?.IsChecked == true;

        var customScale =
            VlcScaleCheck?.IsChecked == true;

        var hdrEnabled =
            VlcHdrCheck?.IsChecked == true;

        VlcScaleCheck!.IsEnabled =
            !fullscreen;

        VlcScaleBox!.IsEnabled =
            customScale && !fullscreen;

        VlcHdrModeBox!.IsEnabled =
            hdrEnabled;

        var permanentNr =
            PermanentVideoNrCheck?.IsChecked == true;

        var permanentArtifacts =
            PermanentArtifactReductionCheck?.IsChecked == true;

        var permanentAi =
            PermanentVideoAiCheck?.IsChecked == true;

        var permanentScale =
            PermanentVideoScaleCheck?.IsChecked == true;

        PermanentVideoScaleBox.IsEnabled =
            permanentScale;

        PermanentVideoModelBox.IsEnabled =
            permanentAi;

        if (PermanentVideoEnhanceButton != null)
        {
            PermanentVideoEnhanceButton.IsEnabled =
                permanentNr || permanentArtifacts || permanentAi;
        }

        if (VlcLaunchStatusText != null)
        {
            var parts = new List<string>
            {
                VlcVsrCheck?.IsChecked == true
                    ? "VSR ON"
                    : "VSR OFF",
                VlcArtifactReductionCheck?.IsChecked == true
                    ? L("artifacts ON", "artefacts ON")
                    : L("artifacts OFF", "artefacts OFF"),
                hdrEnabled
                    ? "HDR ON"
                    : "HDR OFF",
                fullscreen
                    ? L("fullscreen", "plein écran")
                    : customScale
                        ? L("custom scale", "échelle personnalisée")
                        : L("default VLC scale", "échelle VLC par défaut"),
                VlcStatusOverlayCheck?.IsChecked == true
                    ? L("OSD indicator ON", "indicateur OSD ON")
                    : L("OSD indicator OFF", "indicateur OSD OFF")
            };

            VlcLaunchStatusText.Text =
                string.Join(" • ", parts);
        }

        if (PermanentVideoStatusText != null)
        {
            var scaleLabel = permanentScale
                ? L("custom scale", "échelle personnalisée")
                : L("native x1", "x1 natif");

            PermanentVideoStatusText.Text =
                !permanentNr && !permanentArtifacts && !permanentAi
                    ? L("Enable at least Neural Rendering, artifact reduction or AI Upscale.", "Active au moins Neural Rendering, réduction des artefacts ou Upscale IA.")
                    : $"Neural Rendering {(permanentNr ? "ON" : "OFF")} • Artefacts {(permanentArtifacts ? "ON" : "OFF")} • Upscale IA {(permanentAi ? "ON" : "OFF")} • {scaleLabel}.";
        }
    }

    private void LaunchVlcEnhanced_Click(
        object sender,
        RoutedEventArgs e)
    {
        var source = VlcMediaBox.Text;
        if (string.IsNullOrWhiteSpace(source) ||
            !File.Exists(source))
        {
            MessageBox.Show(
                L("Select a local video first.", "Sélectionnez d’abord une vidéo locale."),
                L("VSR-HDR Video", "VSR-HDR Vidéo"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var hdrMode = VlcHdrModeBox.SelectedIndex switch
        {
            1 => VlcHdrMode.Generate,
            2 => VlcHdrMode.Always,
            3 => VlcHdrMode.Never,
            _ => VlcHdrMode.Auto
        };

        var scale = VlcScaleBox.SelectedIndex switch
        {
            1 => VlcScaleMode.X2,
            2 => VlcScaleMode.X4,
            _ => VlcScaleMode.X1
        };

        var fullscreen =
            VlcFullscreenCheck.IsChecked == true;

        try
        {
            _ = _vlcEnhancement.Launch(
                source,
                new VlcLaunchOptions(
                    VlcVsrCheck.IsChecked == true,
                    VlcArtifactReductionCheck.IsChecked == true,
                    VlcHdrCheck.IsChecked == true,
                    hdrMode,
                    VlcScaleCheck.IsChecked == true,
                    scale,
                    fullscreen,
                    VlcStatusOverlayCheck.IsChecked == true));

            var vsr = VlcVsrCheck.IsChecked == true
                ? "VSR ON"
                : "VSR OFF";

            var artifacts = VlcArtifactReductionCheck.IsChecked == true
                ? L("artifacts ON", "artefacts ON")
                : L("artifacts OFF", "artefacts OFF");

            var hdr = VlcHdrCheck.IsChecked == true
                ? hdrMode switch
                {
                    VlcHdrMode.Generate => "SDR→HDR ON",
                    VlcHdrMode.Always => L("HDR forced", "HDR forcé"),
                    VlcHdrMode.Never => L("HDR disabled", "HDR désactivé"),
                    _ => L("HDR auto", "HDR auto")
                }
                : "HDR OFF";

            var target = fullscreen
                ? L("fullscreen / monitor resolution", "plein écran / résolution écran")
                : VlcScaleCheck.IsChecked == true
                    ? L($"window x{(int)scale}", $"fenêtre x{(int)scale}")
                    : L("default VLC scale", "échelle VLC par défaut");

            VlcLaunchStatusText.Text =
                L($"VLC started • {vsr} • {artifacts} • {hdr} • {target}.", $"VLC démarré • {vsr} • {artifacts} • {hdr} • {target}.");
        }
        catch (Exception ex)
        {
            VlcLaunchStatusText.Text =
                L($"Unable to launch enhanced VLC playback: {ex.Message}", $"Impossible de lancer la lecture VLC améliorée : {ex.Message}");

            MessageBox.Show(
                ex.Message,
                "VSR-HDR Vidéo",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SelectPermanentVideo_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L("Select a video to enhance permanently", "Sélectionnez une vidéo à améliorer de façon permanente"),
            Filter =
                "Videos|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.ts;*.m2ts;*.mpeg;*.mpg|" +
                "All files|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        PermanentVideoSourceBox.Text =
            dialog.FileName;

        if (string.IsNullOrWhiteSpace(
                PermanentVideoOutputBox.Text))
        {
            var parent =
                Path.GetDirectoryName(dialog.FileName)
                ?? Environment.GetFolderPath(
                    Environment.SpecialFolder.MyVideos);

            PermanentVideoOutputBox.Text =
                Path.Combine(
                    parent,
                    "DLSS-NR-Enhanced");
        }

        PermanentVideoStatusText.Text =
            L("Video selected • choose x1, x2 or x4 and start enhancement.", "Vidéo sélectionnée • choisissez x1, x2 ou x4 puis lancez l’amélioration.");
    }

    private void SelectPermanentVideoOutput_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = L("Select permanent enhanced-video output folder", "Sélectionnez le dossier de sortie de la vidéo améliorée")
        };

        if (dialog.ShowDialog() == true)
        {
            PermanentVideoOutputBox.Text =
                dialog.FolderName;
        }
    }

    private async void StartPermanentVideoEnhancement_Click(
        object sender,
        RoutedEventArgs e)
    {
        var source =
            PermanentVideoSourceBox.Text;

        if (string.IsNullOrWhiteSpace(source) ||
            !File.Exists(source))
        {
            MessageBox.Show(
                L("Select a local video first.", "Sélectionnez d’abord une vidéo locale."),
                L("Restore HD Video", "Restore HD Vidéo"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var output =
            PermanentVideoOutputBox.Text;

        if (string.IsNullOrWhiteSpace(output))
        {
            var parent =
                Path.GetDirectoryName(source)
                ?? Environment.GetFolderPath(
                    Environment.SpecialFolder.MyVideos);

            output =
                Path.Combine(
                    parent,
                    "DLSS-NR-Enhanced");

            PermanentVideoOutputBox.Text =
                output;
        }

        Directory.CreateDirectory(output);

        var useNeuralRendering =
            PermanentVideoNrCheck.IsChecked == true;

        var useArtifactReduction =
            PermanentArtifactReductionCheck.IsChecked == true;

        var useAiUpscale =
            PermanentVideoAiCheck.IsChecked == true;

        if (!useNeuralRendering &&
            !useArtifactReduction &&
            !useAiUpscale)
        {
            MessageBox.Show(
                L("Enable at least Neural Rendering, artifact reduction or AI Upscale.", "Active au moins Neural Rendering, réduction des artefacts ou Upscale IA."),
                L("VSR-HDR Video", "VSR-HDR Vidéo"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var useCustomScale =
            PermanentVideoScaleCheck.IsChecked == true;

        var scale = useCustomScale
            ? PermanentVideoScaleBox.SelectedIndex switch
            {
                1 => 2,
                2 => 4,
                _ => 1
            }
            : 1;

        if (useAiUpscale && scale == 1)
        {
            MessageBox.Show(
                L("AI Upscale requires x2 or x4 output scale. Enable Output scale and choose x2 or x4, or disable AI Upscale.", "Upscale IA nécessite une échelle x2 ou x4. Active Échelle de sortie et choisis x2 ou x4, ou désactive Upscale IA."),
                L("VSR-HDR Video", "VSR-HDR Vidéo"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var model = PermanentVideoModelBox.SelectedIndex switch
        {
            1 => AiUpscaleModel.GeneralSoft,
            2 => AiUpscaleModel.AnimeIllustration,
            3 => AiUpscaleModel.AnimeVideo,
            _ => AiUpscaleModel.GeneralPhoto
        };

        _permanentVideoCts?.Cancel();
        _permanentVideoCts?.Dispose();
        _permanentVideoCts =
            new CancellationTokenSource();

        var cancellationToken =
            _permanentVideoCts.Token;

        string? temporaryRoot = null;

        try
        {
            PermanentVideoEnhanceButton.IsEnabled = false;
            PermanentVideoCancelButton.IsEnabled = true;

            var progress =
                new Progress<string>(
                    message =>
                        PermanentVideoStatusText.Text =
                            message);

            if (!_media.IsReady)
            {
                PermanentVideoStatusText.Text =
                    L("Preparing manager-owned media engine…", "Préparation du moteur média géré…");
                await _media.SetupAsync(
                    progress,
                    cancellationToken);
            }

            string result;

            if (useAiUpscale)
            {
                var aiInput = source;

                if (useNeuralRendering || useArtifactReduction)
                {
                    temporaryRoot = Path.Combine(
                        Path.GetTempPath(),
                        "DlssNrManager",
                        "permanent-video",
                        Guid.NewGuid().ToString("N"));

                    Directory.CreateDirectory(
                        temporaryRoot);

                    PermanentVideoStatusText.Text =
                        useNeuralRendering
                            ? L("Neural Rendering / cleanup in progress…", "Neural Rendering / nettoyage en cours…")
                            : L("Artifact reduction in progress…", "Réduction des artefacts en cours…");

                    aiInput = await _media.ProcessAsync(
                        source,
                        new MediaProcessOptions(
                            "Native",
                            0,
                            useNeuralRendering ? 1.0 : 0.65,
                            temporaryRoot),
                        progress,
                        cancellationToken);
                }

                PermanentVideoStatusText.Text =
                    L($"Permanent AI Upscale x{scale} in progress…", $"Upscale IA permanent x{scale} en cours…");

                result = await _aiUpscale.UpscaleAsync(
                    aiInput,
                    new AiUpscaleOptions(
                        scale,
                        model,
                        output),
                    _media,
                    progress,
                    cancellationToken);
            }
            else
            {
                var nrScale = scale switch
                {
                    2 => "2x",
                    4 => "4x",
                    _ => "Native"
                };

                PermanentVideoStatusText.Text =
                    useNeuralRendering
                        ? L($"Permanent Neural Rendering {nrScale} in progress…", $"Neural Rendering permanent {nrScale} en cours…")
                        : L($"Permanent artifact reduction {nrScale} in progress…", $"Réduction des artefacts permanente {nrScale} en cours…");

                result = await _media.ProcessAsync(
                    source,
                    new MediaProcessOptions(
                        nrScale,
                        0,
                        useNeuralRendering ? 1.0 : 0.65,
                        output),
                    progress,
                    cancellationToken);
            }

            PermanentVideoStatusText.Text =
                L($"Permanent enhancement complete • {result}", $"Amélioration permanente terminée • {result}");
        }
        catch (OperationCanceledException)
        {
            PermanentVideoStatusText.Text =
                L("Permanent video enhancement cancelled.", "Amélioration vidéo permanente annulée.");
        }
        catch (Exception ex)
        {
            PermanentVideoStatusText.Text =
                L($"Permanent enhancement failed: {ex.Message}", $"Échec de l’amélioration permanente : {ex.Message}");

            MessageBox.Show(
                ex.Message,
                L("Restore HD Video", "Restore HD Vidéo"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryRoot))
            {
                try
                {
                    if (Directory.Exists(temporaryRoot))
                        Directory.Delete(temporaryRoot, true);
                }
                catch
                {
                }
            }

            _permanentVideoCts?.Dispose();
            _permanentVideoCts = null;
            PermanentVideoEnhanceButton.IsEnabled = true;
            PermanentVideoCancelButton.IsEnabled = false;
        }
    }

    private void CancelPermanentVideoEnhancement_Click(
        object sender,
        RoutedEventArgs e)
    {
        _permanentVideoCts?.Cancel();
        PermanentVideoStatusText.Text =
            L("Cancelling permanent video enhancement…", "Annulation de l’amélioration vidéo permanente…");
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

    private void OpenDownloadsCenter_Click(
        object sender,
        RoutedEventArgs e)
    {
        // Menu order is kept in sync with the main TabControl.
        const int downloadsMenuIndex = 6;
        MainMenuList.SelectedIndex =
            downloadsMenuIndex;
        RefreshDownloadCenter();
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
            : _aiOrigin.IsInstalled
                ? "Media selected • models installed; verify them before analysis."
                : "Media selected • set up the detector before analysis.";
    }

    private void ClearAiOriginMedia_Click(object sender, RoutedEventArgs e)
    {
        AiOriginSourceBox.Clear();
        AiOriginClearMediaButton.IsEnabled = false;
        AiOriginStatusText.Text = _aiOrigin.IsReady
            ? "No media selected • detector ready."
            : _aiOrigin.IsInstalled
                ? "No media selected • detector installed, verification pending."
                : "No media selected • detector not set up yet.";
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
            MediaProcessButton.IsEnabled = false;

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
            MediaProcessButton.IsEnabled = true;
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

    private void AutoUpdateComponents_Changed(object sender, RoutedEventArgs e)
    {
        // Checked/Unchecked can fire while InitializeComponent is constructing
        // the UI; only user changes after initialization persist preferences.
        if (!_languageSelectorReady || AutoUpdateComponentsCheck == null)
            return;

        try
        {
            var current = AppPreferencesService.Load();
            AppPreferencesService.Save(
                current with
                {
                    AutoUpdateComponents =
                        AutoUpdateComponentsCheck.IsChecked == true
                });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unable to save component auto-update preference.", ex);
        }
    }

    private void LanguageSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_languageSelectorReady ||
            LanguageSelector.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string requestedLanguage)
        {
            return;
        }

        _uiLanguage = UiLocalizationService.NormalizeLanguage(
            requestedLanguage);

        var software =
            System.Windows.Media.RenderOptions.ProcessRenderMode ==
            System.Windows.Interop.RenderMode.SoftwareOnly;

        AppPreferencesService.Save(
            new AppPreferences(
                software,
                _uiLanguage,
                AutoUpdateComponentsCheck.IsChecked == true));

        _localization?.Apply();

        AppLogger.Info(
            "UI language changed to " + _uiLanguage + ".");
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
            new AppPreferences(
                software,
                _uiLanguage,
                AutoUpdateComponentsCheck.IsChecked == true));

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

    private void InitializeAiStudioUi()
    {
        if (AiStudioTaskBox == null ||
            AiStudioModelBox == null ||
            AiStudioBackendBox == null)
        {
            return;
        }

        AiStudioTaskBox.ItemsSource =
            LocalAiStudioService.TaskChoices;
        AiStudioBackendBox.SelectedIndex = 0;
        AiStudioOutputBox.Text = _aiStudio.OutputsRoot;

        if (AiStudioTaskBox.Items.Count > 0)
            AiStudioTaskBox.SelectedIndex = 0;

        RefreshAiStudioModelManager();
        RefreshAiStudioJobs();
        RefreshAiStudioRuntimeStatus();
    }

    private AiStudioTaskChoice? SelectedAiStudioTask()
        => AiStudioTaskBox?.SelectedItem as AiStudioTaskChoice;

    private AiStudioModelDescriptor? SelectedAiStudioModel()
        => AiStudioModelBox?.SelectedItem as AiStudioModelDescriptor;

    private AiStudioBackend SelectedAiStudioBackend()
        => AiStudioBackendBox?.SelectedIndex switch
        {
            1 => AiStudioBackend.ComfyUi,
            2 => AiStudioBackend.Diffusers,
            _ => AiStudioBackend.Auto
        };

    private void AiStudioTaskBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        RefreshAiStudioModelChoices();
    }

    private void RefreshAiStudioModelChoices()
    {
        if (AiStudioTaskBox == null ||
            AiStudioModelBox == null)
        {
            return;
        }

        var task = SelectedAiStudioTask();
        if (task == null)
        {
            AiStudioModelBox.ItemsSource = null;
            return;
        }

        var models = _aiStudio.GetModels(task.Task);
        AiStudioModelBox.ItemsSource = models;

        var preferred =
            models.FirstOrDefault(x =>
                x.Tier == AiStudioModelTier.Recommended) ??
            models.FirstOrDefault();

        if (preferred != null)
            AiStudioModelBox.SelectedItem = preferred;

        var requiresInput =
            task.Task is
                AiStudioTaskKind.ImageToImage or
                AiStudioTaskKind.InpaintOutpaint or
                AiStudioTaskKind.ImageToVideo or
                AiStudioTaskKind.VideoToVideo;

        var usesMask =
            task.Task == AiStudioTaskKind.InpaintOutpaint;

        if (AiStudioInputBox != null)
            AiStudioInputBox.IsEnabled = requiresInput;

        if (AiStudioMaskBox != null)
            AiStudioMaskBox.IsEnabled = usesMask;
    }

    private void AiStudioModelBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        RefreshSelectedAiStudioModelDetails();
    }

    private void RefreshSelectedAiStudioModelDetails()
    {
        var model = SelectedAiStudioModel();
        if (model == null ||
            AiStudioModelDetailsText == null)
        {
            return;
        }

        var backends = string.Join(
            ", ",
            model.Backends.Select(x =>
                x == AiStudioBackend.ComfyUi
                    ? "ComfyUI"
                    : "Diffusers"));

        AiStudioModelDetailsText.Text =
            $"{UiLocalizationService.Translate(model.QualityLabel, _uiLanguage)} • {model.License} • {backends}\n" +
            $"{UiLocalizationService.Translate(model.HardwareLabel, _uiLanguage)}\n" +
            $"{UiLocalizationService.Translate(model.Notes, _uiLanguage)}\n" +
            $"{L("Model", "Modèle")}: {model.Repository}";
    }

    private void AiStudioModelList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (AiStudioModelList?.SelectedItem
            is not AiStudioModelDescriptor model)
        {
            return;
        }

        AiStudioManagerModelText.Text =
            $"{model.DisplayName}\n" +
            $"{L("Tier", "Niveau")}: {model.Tier}\n" +
            $"{L("Tasks", "Tâches")}: {string.Join(", ", model.Tasks)}\n" +
            $"Backends: {string.Join(", ", model.Backends)}\n" +
            $"{L("Repository", "Dépôt")}: {model.Repository}\n" +
            $"{L("License", "Licence")}: {model.License}\n" +
            $"{L("Hardware", "Matériel")}: {UiLocalizationService.Translate(model.HardwareLabel, _uiLanguage)}\n\n" +
            UiLocalizationService.Translate(model.Notes, _uiLanguage);

        AiStudioManagerModelStatusText.Text =
            _aiStudio.GetModelStatus(
                model,
                _uiLanguage);
    }

    private void RefreshAiStudioModels_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshAiStudioModelManager();
    }

    private void RefreshAiStudioModelManager()
    {
        if (AiStudioModelList == null)
            return;

        var selectedId =
            (AiStudioModelList.SelectedItem
                as AiStudioModelDescriptor)?.Id;

        AiStudioModelList.ItemsSource =
            LocalAiStudioService.Models;

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            AiStudioModelList.SelectedItem =
                LocalAiStudioService.Models
                    .FirstOrDefault(x => x.Id == selectedId);
        }

        AiStudioModelList.SelectedItem ??=
            LocalAiStudioService.Models.FirstOrDefault();
    }

    private void SelectAiStudioInput_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L("Select AI Studio source", "Sélectionnez la source AI Studio"),
            Filter =
                "Media files|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.mp4;*.mkv;*.mov;*.webm|All files|*.*"
        };

        if (dialog.ShowDialog(this) == true)
            AiStudioInputBox.Text = dialog.FileName;
    }

    private void SelectAiStudioMask_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L("Select inpainting mask", "Sélectionnez le masque d’inpainting"),
            Filter =
                "Image files|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*"
        };

        if (dialog.ShowDialog(this) == true)
            AiStudioMaskBox.Text = dialog.FileName;
    }

    private void SelectAiStudioOutput_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = L("Select AI Studio output folder", "Sélectionnez le dossier de sortie AI Studio"),
            InitialDirectory =
                Directory.Exists(AiStudioOutputBox.Text)
                    ? AiStudioOutputBox.Text
                    : _aiStudio.OutputsRoot
        };

        if (dialog.ShowDialog(this) == true)
            AiStudioOutputBox.Text = dialog.FolderName;
    }

    private void QueueAiStudioJob_Click(
        object sender,
        RoutedEventArgs e)
    {
        var task = SelectedAiStudioTask();
        var model = SelectedAiStudioModel();

        if (task == null || model == null)
        {
            MessageBox.Show(
                L("Select a task and a model first.", "Sélectionnez d’abord une tâche et un modèle."),
                "AI Studio local",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var requiresInput =
            task.Task is
                AiStudioTaskKind.ImageToImage or
                AiStudioTaskKind.InpaintOutpaint or
                AiStudioTaskKind.ImageToVideo or
                AiStudioTaskKind.VideoToVideo;

        if (requiresInput &&
            (string.IsNullOrWhiteSpace(AiStudioInputBox.Text) ||
             !File.Exists(AiStudioInputBox.Text)))
        {
            MessageBox.Show(
                L("This task requires a valid source image or video.", "Cette tâche nécessite une image ou une vidéo source valide."),
                "AI Studio local",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var output = string.IsNullOrWhiteSpace(
            AiStudioOutputBox.Text)
            ? _aiStudio.OutputsRoot
            : AiStudioOutputBox.Text;

        var job = _aiStudio.QueueJob(
            task.Task,
            model,
            SelectedAiStudioBackend(),
            AiStudioPromptBox.Text ?? string.Empty,
            string.IsNullOrWhiteSpace(AiStudioInputBox.Text)
                ? null
                : AiStudioInputBox.Text,
            string.IsNullOrWhiteSpace(AiStudioMaskBox.Text)
                ? null
                : AiStudioMaskBox.Text,
            output);

        AiStudioStatusText.Text =
            L(
                $"Job {job.Id:N} queued • {model.DisplayName} • {task.Label}. Execution starts only after the isolated manager-owned runtime and selected model are installed.",
                $"Job {job.Id:N} ajouté à la file • {model.DisplayName} • {task.Label}. L’exécution démarre uniquement lorsque le runtime isolé géré et le modèle sélectionné sont installés.");

        RefreshAiStudioJobs();
    }

    private void RefreshAiStudioJobs_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshAiStudioJobs();
    }

    private void RefreshAiStudioJobs()
    {
        if (AiStudioJobList == null)
            return;

        AiStudioJobList.ItemsSource =
            _aiStudio.LoadJobs()
                .Select(x =>
                    $"{x.CreatedAt.LocalDateTime:g} • {x.Task} • {x.ModelId} • {x.Status} • {x.Id:N}")
                .ToArray();
    }

    private void PrepareAiStudioWorkspace_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _aiStudio.EnsureWorkspace();
            RefreshAiStudioRuntimeStatus();

            AiStudioStatusText.Text =
                L(
                    "Isolated AI Studio workspace prepared. Python/PyTorch/CUDA/ComfyUI/Diffusers remain manager-owned components and are not installed globally.",
                    "Workspace AI Studio isolé préparé. Python/PyTorch/CUDA/ComfyUI/Diffusers restent des composants gérés par l’application et ne sont pas installés globalement.");
        }
        catch (Exception ex)
        {
            AiStudioRuntimeStatusText.Text =
                L(
                    $"Unable to prepare AI Studio workspace: {ex.Message}",
                    $"Impossible de préparer le workspace AI Studio : {ex.Message}");
        }
    }

    private void RefreshAiStudioRuntimeStatus()
    {
        if (AiStudioRuntimeStatusText != null)
            AiStudioRuntimeStatusText.Text =
                _aiStudio.GetRuntimeSummary(
                    _uiLanguage);

        if (AiStudioRuntimeSummaryText != null)
            AiStudioRuntimeSummaryText.Text =
                _aiStudio.GetRuntimeSummary();
    }

    private void OpenAiStudioModelsFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        _aiStudio.EnsureWorkspace();
        Process.Start(new ProcessStartInfo
        {
            FileName = _aiStudio.ModelsRoot,
            UseShellExecute = true
        });
    }

    private void OpenAiStudioRoot_Click(
        object sender,
        RoutedEventArgs e)
    {
        _aiStudio.EnsureWorkspace();
        Process.Start(new ProcessStartInfo
        {
            FileName = _aiStudio.Root,
            UseShellExecute = true
        });
    }


    private void OnDownloadProgressChanged(
        DownloadProgressSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => OnDownloadProgressChanged(snapshot));
            return;
        }

        if (DownloadProgressPanel == null ||
            GlobalDownloadProgressBar == null ||
            DownloadProgressTitleText == null ||
            DownloadProgressDetailsText == null)
        {
            return;
        }

        if (!snapshot.IsActive)
        {
            DownloadProgressPanel.Visibility =
                Visibility.Collapsed;
            return;
        }

        DownloadProgressPanel.Visibility =
            Visibility.Visible;

        DownloadProgressTitleText.Text =
            snapshot.Label;

        var speedMiB =
            snapshot.BytesPerSecond /
            (1024d * 1024d);

        if (snapshot.Percent is double percent)
        {
            GlobalDownloadProgressBar.IsIndeterminate =
                false;
            GlobalDownloadProgressBar.Value =
                percent;

            DownloadProgressDetailsText.Text =
                $"{percent:0} % • {speedMiB:0.0} {(UiLocalizationService.NormalizeLanguage(_uiLanguage) == "fr" ? "Mo/s" : "MB/s")} • {FormatDownloadEta(snapshot.EstimatedRemaining)}";
        }
        else
        {
            GlobalDownloadProgressBar.IsIndeterminate =
                true;

            DownloadProgressDetailsText.Text =
                $"{speedMiB:0.0} {(UiLocalizationService.NormalizeLanguage(_uiLanguage) == "fr" ? "Mo/s" : "MB/s")} • {L("time remaining: calculating…", "temps restant : calcul…")}";
        }
    }

    private string FormatDownloadEta(
        TimeSpan? remaining)
    {
        if (remaining == null ||
            remaining.Value < TimeSpan.Zero ||
            double.IsNaN(remaining.Value.TotalSeconds) ||
            double.IsInfinity(remaining.Value.TotalSeconds))
        {
            return L("time remaining: calculating…", "temps restant : calcul…");
        }

        var value = remaining.Value;

        if (value.TotalHours >= 1)
        {
            return
                $"{L("remaining", "reste")} {Math.Floor(value.TotalHours):0}:" +
                $"{value.Minutes:00}:{value.Seconds:00}";
        }

        return
            $"{L("remaining", "reste")} {value.Minutes:00}:{value.Seconds:00}";
    }


    private Task<bool> ConfirmLargeDownloadAsync(
        LargeDownloadApprovalRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<bool>(cancellationToken);

        if (Dispatcher.CheckAccess())
            return Task.FromResult(ShowLargeDownloadApproval(request));

        return Dispatcher
            .InvokeAsync(
                () => ShowLargeDownloadApproval(request))
            .Task;
    }

    private bool ShowLargeDownloadApproval(
        LargeDownloadApprovalRequest request)
    {
        var answer = MessageBox.Show(
            this,
            L(
                $"This download exceeds 1 GB.\n\n{request.Label}\nEstimated size: {request.TotalGiB:0.00} GB\n{request.Purpose}\n\nThe download will use bandwidth and disk space.\n\nDo you want to continue?",
                $"Ce téléchargement dépasse 1 Go.\n\n{request.Label}\nTaille estimée : {request.TotalGiB:0.00} Go\n{request.Purpose}\n\nLe téléchargement utilisera de la bande passante et de l'espace disque.\n\nVoulez-vous continuer ?"),
            L("Large download", "Téléchargement volumineux"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return answer == MessageBoxResult.Yes;
    }


    private DownloadCenterEntry? SelectedDownloadCenterEntry()
        => DownloadCenterList?.SelectedItem
            as DownloadCenterEntry;

    private async void RefreshDownloadCenter_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshDownloadCenter();
        if (_downloadCenterCts != null || _checkingDownloadCenterUpdates)
            return;

        _checkingDownloadCenterUpdates = true;
        _mediaUpdateStatus = MediaUpdateAvailability.UnknownLocalVersion;
        _modelUpdateStatus = MediaUpdateAvailability.UnknownLocalVersion;
        _modelUpdateEntryId = null;
        RefreshDownloadCenterButtons();

        try
        {
            DownloadCenterStatusText.Text = L(
                "Checking validated media component versions…",
                "Vérification des versions validées du moteur média…");

            _mediaUpdateStatus = await _components.CheckMediaUpdateAsync(_media);
            DownloadCenterStatusText.Text = _mediaUpdateStatus switch
            {
                MediaUpdateAvailability.UpdateAvailable => L(
                    "Validated media update available in Téléchargements.",
                    "Mise à jour média validée disponible dans Téléchargements."),
                MediaUpdateAvailability.UpToDate => L(
                    "Media engine is up to date.", "Le moteur média est à jour."),
                MediaUpdateAvailability.NotInstalled => L(
                    "Media engine is not installed.", "Le moteur média n'est pas installé."),
                MediaUpdateAvailability.UnknownLocalVersion => L(
                    "Installed media version is unknown; no update is offered. Redownload remains available.",
                    "Version du moteur média inconnue ; aucune mise à jour n'est proposée. Le retéléchargement reste disponible."),
                _ => L(
                    "Release manifest could not confirm a newer media version.",
                    "Le manifeste de release ne confirme pas de nouvelle version média.")
            };

            var selected = SelectedDownloadCenterEntry();
            if (selected is { Kind: DownloadCenterKind.AiStudioModel,
                              IsInstalled: true, CanInstallAutomatically: true })
            {
                _modelUpdateStatus = await _downloadCenter.CheckAiStudioModelUpdateAsync(selected);
                _modelUpdateEntryId = selected.Id;
                DownloadCenterStatusText.Text = _modelUpdateStatus switch
                {
                    MediaUpdateAvailability.UpdateAvailable => L(
                        $"Validated update available for {selected.DisplayName}.",
                        $"Mise à jour validée disponible pour {selected.DisplayName}."),
                    MediaUpdateAvailability.UpToDate => L(
                        $"{selected.DisplayName} is up to date.",
                        $"{selected.DisplayName} est à jour."),
                    MediaUpdateAvailability.UnknownLocalVersion => L(
                        "This local model has no verified package receipt. Update is not offered; repair remains available.",
                        "Ce modèle n'a pas de reçu de package vérifié. Aucune mise à jour proposée ; la réparation reste possible."),
                    _ => L(
                        "No validated newer model package is available.",
                        "Aucun nouveau package de modèle validé disponible.")
                };
            }
        }
        catch (Exception ex)
        {
            _mediaUpdateStatus = MediaUpdateAvailability.UnknownRemoteVersion;
            _modelUpdateStatus = MediaUpdateAvailability.UnknownRemoteVersion;
            AppLogger.Warn("Download Center update check unavailable: " + ex.Message);
            DownloadCenterStatusText.Text = L(
                "Update check unavailable. Installed components were not changed.",
                "Vérification des mises à jour indisponible. Aucun composant installé n'a été modifié.");
        }
        finally
        {
            _checkingDownloadCenterUpdates = false;
            RefreshDownloadCenterButtons();
        }
    }

    private sealed record OfficialUpdateDisplay(
        OfficialUpstreamResult Update, string Label)
    {
        public override string ToString() => Label;
    }

    private async void CheckOfficialUpstreamUpdates_Click(
        object sender, RoutedEventArgs e)
    {
        if (_checkingOfficialUpdates)
            return;

        _checkingOfficialUpdates = true;
        CheckOfficialUpstreamUpdatesButton.IsEnabled = false;
        OfficialUpdateStatusText.Text = L(
            "Checking official GitHub repositories (read-only)…",
            "Vérification des dépôts GitHub officiels (lecture seule)…");

        try
        {
            var results = await _officialUpdates.CheckAsync();
            var items = results.Select(result =>
            {
                var status = result.State switch
                {
                    OfficialUpstreamState.NewVersion =>
                        L("new official version", "nouvelle version officielle"),
                    OfficialUpstreamState.SourceChanged =>
                        L("source revision changed", "révision source différente"),
                    OfficialUpstreamState.UpToDate =>
                        L("tracked source current", "source suivie inchangée"),
                    OfficialUpstreamState.ReviewRequired =>
                        L("manual review needed", "vérification manuelle nécessaire"),
                    _ => L("unavailable", "indisponible")
                };
                var latest = string.IsNullOrWhiteSpace(result.RemoteReference)
                    ? "" : " • " + result.RemoteReference;
                return new OfficialUpdateDisplay(
                    result, result.Source.Id + " — " + status + latest);
            }).OrderByDescending(x => x.Update.RequiresReview)
              .ThenBy(x => x.Update.Source.Id, StringComparer.OrdinalIgnoreCase)
              .ToArray();

            OfficialUpdateList.ItemsSource = items;
            var count = results.Count(x => x.RequiresReview);
            OfficialUpdateStatusText.Text = L(
                $"{count} official changes to review out of {results.Count} tracked sources. Installing updates still requires validated manager-owned packages.",
                $"{count} modifications officielles à examiner sur {results.Count} sources suivies. L'installation exige des packages validés par le Manager.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Official source check unavailable: " + ex.Message);
            OfficialUpdateStatusText.Text = L(
                "Source lookup unavailable. Installed components were not changed.",
                "Sources indisponibles. Aucun composant installé n'a été modifié.");
        }
        finally
        {
            _checkingOfficialUpdates = false;
            CheckOfficialUpstreamUpdatesButton.IsEnabled = true;
        }
    }

    private void OfficialUpdateList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        var item = OfficialUpdateList.SelectedItem as OfficialUpdateDisplay;
        OpenOfficialUpstreamButton.IsEnabled = item != null;
        if (item == null)
        {
            OfficialUpdateDetailsText.Text = "";
            return;
        }

        OfficialUpdateDetailsText.Text =
            item.Update.Source.Repository + "\n" +
            L("Pinned project reference: ", "Référence épinglée du projet : ") +
            (item.Update.Source.LockedTag ?? item.Update.Source.LockedRef) + "\n" +
            L("Promotion policy: ", "Politique de validation : ") +
            item.Update.Source.Promotion + "\n" +
            L("Upstream discovery does not authorize an installation. Only approved manager-owned packages can be installed.",
              "La détection amont n'autorise aucune installation. Seuls les packages approuvés du Manager peuvent être installés.");
    }

    private void OpenOfficialUpstream_Click(
        object sender, RoutedEventArgs e)
    {
        if (OfficialUpdateList.SelectedItem is not OfficialUpdateDisplay item ||
            !Uri.TryCreate(item.Update.OfficialUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Cannot open official repository: " + ex.Message);
            OfficialUpdateStatusText.Text = L(
                "Unable to open official repository.",
                "Impossible d'ouvrir le dépôt officiel.");
        }
    }

    private void RefreshDownloadCenter()
    {
        if (DownloadCenterList == null)
            return;

        var selectedId =
            (DownloadCenterList.SelectedItem
                as DownloadCenterEntry)?.Id;

        var entries =
            _downloadCenter.GetEntries(_uiLanguage);

        DownloadCenterList.ItemsSource =
            entries;

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            DownloadCenterList.SelectedItem =
                entries.FirstOrDefault(
                    x => x.Id == selectedId);
        }

        DownloadCenterList.SelectedItem ??=
            entries.FirstOrDefault();

        RefreshDownloadCenterButtons();
    }

    private async void DownloadCenterList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        await RefreshDownloadCenterSelectionAsync();
    }

    private async Task RefreshDownloadCenterSelectionAsync()
    {
        var entry =
            SelectedDownloadCenterEntry();

        if (entry == null)
        {
            DownloadCenterDetailsText.Text =
                L("Select a download.", "Sélectionnez un téléchargement.");
            RefreshDownloadCenterButtons();
            return;
        }

        DownloadCenterDetailsText.Text =
            $"{entry.DisplayName}\n{L("Loading details…", "Chargement des détails…")}";

        try
        {
            DownloadCenterDetailsText.Text =
                await _downloadCenter.GetDetailsAsync(
                    entry,
                    _uiLanguage);
        }
        catch (Exception ex)
        {
            DownloadCenterDetailsText.Text =
                $"{entry.DisplayName}\n\n{L("Remote details unavailable", "Détails distants indisponibles")} : {ex.Message}";
        }

        RefreshDownloadCenterButtons();
    }

    private void RefreshDownloadCenterButtons()
    {
        if (DownloadCenterInstallButton == null ||
            DownloadCenterRemoveButton == null ||
            DownloadCenterRedownloadButton == null)
        {
            return;
        }

        var entry =
            SelectedDownloadCenterEntry();

        if (entry == null)
        {
            DownloadCenterInstallButton.IsEnabled =
                false;
            DownloadCenterRemoveButton.IsEnabled =
                false;
            DownloadCenterRedownloadButton.IsEnabled =
                false;
            return;
        }

        var busy =
            _downloadCenterCts != null || _checkingDownloadCenterUpdates;

        var isModelUpdate =
            entry.Kind == DownloadCenterKind.AiStudioModel &&
            entry.Id == _modelUpdateEntryId &&
            entry.CanInstallAutomatically &&
            _modelUpdateStatus == MediaUpdateAvailability.UpdateAvailable;
        DownloadCenterRedownloadButton.Content =
            (entry.Kind == DownloadCenterKind.MediaEngine &&
             _mediaUpdateStatus == MediaUpdateAvailability.UpdateAvailable) ||
            isModelUpdate
                ? L("Update", "Mettre à jour")
                : entry.RequiresLicenseAcceptance
                    ? L("Reimport", "Réimporter")
                    : L("Redownload", "Retélécharger");

        DownloadCenterInstallButton.IsEnabled =
            !busy &&
            !entry.IsInstalled &&
            (entry.CanInstallAutomatically ||
             entry.RequiresLicenseAcceptance);

        DownloadCenterRemoveButton.IsEnabled =
            !busy &&
            entry.IsInstalled;

        DownloadCenterRedownloadButton.IsEnabled =
            !busy &&
            entry.IsInstalled &&
            (entry.CanInstallAutomatically ||
             entry.RequiresLicenseAcceptance);
    }

    private async void InstallDownloadCenter_Click(
        object sender,
        RoutedEventArgs e)
    {
        var entry =
            SelectedDownloadCenterEntry();

        if (entry == null)
            return;

        await RunDownloadCenterOperationAsync(
            entry,
            redownload: false);
    }

    private async void RedownloadDownloadCenter_Click(
        object sender,
        RoutedEventArgs e)
    {
        var entry =
            SelectedDownloadCenterEntry();

        if (entry == null)
            return;

        var isMediaUpdate =
            entry.Kind == DownloadCenterKind.MediaEngine &&
            _mediaUpdateStatus == MediaUpdateAvailability.UpdateAvailable;
        var isModelUpdate =
            entry.Kind == DownloadCenterKind.AiStudioModel &&
            entry.CanInstallAutomatically &&
            entry.Id == _modelUpdateEntryId &&
            _modelUpdateStatus == MediaUpdateAvailability.UpdateAvailable;

        var answer = MessageBox.Show(
            this,
            entry.RequiresLicenseAcceptance
                ? L(
                    $"Reimport {entry.DisplayName}?\n\nThe current local copy will be replaced by the official files you select.",
                    $"Réimporter {entry.DisplayName} ?\n\nLa copie locale actuelle sera remplacée par les fichiers officiels que vous sélectionnerez.")
                : isMediaUpdate || isModelUpdate
                    ? L(
                        $"Update {entry.DisplayName}?\n\nThe installed copy is backed up and restored if the update fails.",
                        $"Mettre à jour {entry.DisplayName} ?\n\nLa copie installée est sauvegardée et restaurée si la mise à jour échoue.")
                    : L(
                        $"Redownload {entry.DisplayName}?\n\nThe installed copy is backed up and restored if the replacement fails.",
                        $"Retélécharger {entry.DisplayName} ?\n\nLa copie installée est sauvegardée et restaurée si le remplacement échoue."),
            entry.RequiresLicenseAcceptance
                ? L("Reimport", "Réimporter")
                : isMediaUpdate || isModelUpdate
                    ? L("Update", "Mettre à jour")
                    : L("Redownload", "Retélécharger"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
            return;

        await RunDownloadCenterOperationAsync(
            entry,
            redownload: true);
    }

    private async Task RunDownloadCenterOperationAsync(
        DownloadCenterEntry entry,
        bool redownload)
    {
        if (entry.RequiresLicenseAcceptance)
        {
            await RunManualLicensedModelInstallAsync(
                entry,
                redownload);
            return;
        }

        if (_downloadCenterCts != null)
            return;

        _downloadCenterCts =
            new CancellationTokenSource();

        DownloadCenterCancelButton.Visibility =
            Visibility.Visible;
        RefreshDownloadCenterButtons();

        var progress =
            new Progress<string>(
                message =>
                {
                    DownloadCenterStatusText.Text =
                        message;
                });

        try
        {
            DownloadCenterStatusText.Text =
                redownload
                    ? L($"Redownloading {entry.DisplayName}…", $"Retéléchargement de {entry.DisplayName}…")
                    : L($"Installing {entry.DisplayName}…", $"Installation de {entry.DisplayName}…");

            if (redownload)
            {
                if (entry.Kind == DownloadCenterKind.MediaEngine &&
                    _mediaUpdateStatus == MediaUpdateAvailability.UpdateAvailable)
                {
                    // Uses MediaService's transactional updater and stores the
                    // validated release fingerprint only after success.
                    await _components.EnsureMediaToolsLatestAsync(
                        _media,
                        progress,
                        _downloadCenterCts.Token,
                        forceRefresh: true);
                }
                else
                {
                    await _downloadCenter.RedownloadAsync(
                        entry,
                        progress,
                        _downloadCenterCts.Token);
                }
            }
            else
            {
                await _downloadCenter.InstallAsync(
                    entry,
                    progress,
                    _downloadCenterCts.Token);
            }

            DownloadCenterStatusText.Text =
                L($"{entry.DisplayName} is ready.", $"{entry.DisplayName} est prêt.");
        }
        catch (OperationCanceledException)
        {
            DownloadCenterStatusText.Text =
                L($"Operation cancelled: {entry.DisplayName}.", $"Opération annulée : {entry.DisplayName}.");
        }
        catch (Exception ex)
        {
            DownloadCenterStatusText.Text =
                L($"Failure: {ex.Message}", $"Échec : {ex.Message}");
        }
        finally
        {
            _downloadCenterCts.Dispose();
            _downloadCenterCts = null;
            if (entry.Kind == DownloadCenterKind.MediaEngine)
                _mediaUpdateStatus = MediaUpdateAvailability.UnknownLocalVersion;
            if (entry.Kind == DownloadCenterKind.AiStudioModel)
            {
                _modelUpdateStatus = MediaUpdateAvailability.UnknownLocalVersion;
                _modelUpdateEntryId = null;
            }
            DownloadCenterCancelButton.Visibility =
                Visibility.Collapsed;

            RefreshDownloadCenter();
            await RefreshDownloadCenterSelectionAsync();
        }
    }

    private async Task RunManualLicensedModelInstallAsync(
        DownloadCenterEntry entry,
        bool redownload)
    {
        if (_downloadCenterCts != null)
            return;

        var licenseInfo =
            _downloadCenter.GetManualLicenseInfo(
                entry);

        if (licenseInfo == null)
        {
            DownloadCenterStatusText.Text =
                L($"No manual license is configured for {entry.DisplayName}.", $"Aucune licence manuelle configurée pour {entry.DisplayName}.");
            return;
        }

        if (!_downloadCenter.IsManualLicenseAccepted(
                entry))
        {
            var dialog =
                new AiStudioLicenseDialog(
                    licenseInfo,
                    _uiLanguage)
                {
                    Owner = this
                };

            var accepted =
                dialog.ShowDialog() == true &&
                dialog.Accepted;

            if (!accepted)
            {
                DownloadCenterStatusText.Text =
                    L($"License declined • {entry.DisplayName} was not installed.", $"Licence refusée • {entry.DisplayName} n'a pas été installé.");
                return;
            }

            _downloadCenter.AcceptManualLicense(
                entry);

            DownloadCenterStatusText.Text =
                L($"License accepted for {entry.DisplayName}. Now select the files obtained from the official source.", $"Licence acceptée pour {entry.DisplayName}. Sélectionnez maintenant les fichiers obtenus depuis la source officielle.");
        }

        var folderDialog =
            new OpenFolderDialog
            {
                Title =
                    L($"Select the official folder for {entry.DisplayName}", $"Sélectionnez le dossier officiel de {entry.DisplayName}")
            };

        if (folderDialog.ShowDialog(this) != true)
        {
            DownloadCenterStatusText.Text =
                L($"Installation cancelled. The license for {entry.DisplayName} remains accepted locally for this version.", $"Installation annulée. La licence de {entry.DisplayName} reste acceptée localement pour cette version.");
            await RefreshDownloadCenterSelectionAsync();
            return;
        }

        _downloadCenterCts =
            new CancellationTokenSource();

        DownloadCenterCancelButton.Visibility =
            Visibility.Visible;
        RefreshDownloadCenterButtons();

        var progress =
            new Progress<string>(
                message =>
                {
                    DownloadCenterStatusText.Text =
                        message;
                });

        try
        {
            DownloadCenterStatusText.Text =
                redownload
                    ? L($"Reimporting {entry.DisplayName}…", $"Réimport de {entry.DisplayName}…")
                    : L($"Importing {entry.DisplayName}…", $"Import de {entry.DisplayName}…");

            await _downloadCenter.ImportManualModelAsync(
                entry,
                folderDialog.FolderName,
                progress,
                _downloadCenterCts.Token);

            DownloadCenterStatusText.Text =
                L($"{entry.DisplayName} is ready and available offline.", $"{entry.DisplayName} est prêt et disponible hors ligne.");
        }
        catch (OperationCanceledException)
        {
            DownloadCenterStatusText.Text =
                L($"Import cancelled: {entry.DisplayName}.", $"Import annulé : {entry.DisplayName}.");
        }
        catch (Exception ex)
        {
            DownloadCenterStatusText.Text =
                L($"Import failed: {ex.Message}", $"Échec de l'import : {ex.Message}");
        }
        finally
        {
            _downloadCenterCts.Dispose();
            _downloadCenterCts = null;
            DownloadCenterCancelButton.Visibility =
                Visibility.Collapsed;

            RefreshDownloadCenter();
            await RefreshDownloadCenterSelectionAsync();
        }
    }

    private void RemoveDownloadCenter_Click(
        object sender,
        RoutedEventArgs e)
    {
        var entry =
            SelectedDownloadCenterEntry();

        if (entry == null ||
            !entry.IsInstalled)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            L(
                $"Remove {entry.DisplayName} from this PC?\n\nThe local files will be deleted. You can download them again later.",
                $"Supprimer {entry.DisplayName} de ce PC ?\n\nLes fichiers locaux seront supprimés. Vous pourrez les retélécharger plus tard."),
            L("Remove local download", "Supprimer le téléchargement local"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            _downloadCenter.Remove(entry);
            DownloadCenterStatusText.Text =
                L($"{entry.DisplayName} removed locally.", $"{entry.DisplayName} supprimé localement.");
        }
        catch (Exception ex)
        {
            DownloadCenterStatusText.Text =
                L($"Unable to remove: {ex.Message}", $"Suppression impossible : {ex.Message}");
        }

        RefreshDownloadCenter();
    }

    private void CancelDownloadCenter_Click(
        object sender,
        RoutedEventArgs e)
    {
        _downloadCenterCts?.Cancel();
        DownloadCenterStatusText.Text =
            L("Cancelling download…", "Annulation du téléchargement…");
    }

    private HardwareProfilePreferences CurrentHardwarePreferences()
        => new(
            HardwareProfileBox.SelectedIndex == 1
                ? HardwareProfileChoice.Rtx5060Ti9700X
                : HardwareProfileChoice.Auto,
            HardwareCompatibleCheck.IsChecked == true
                ? HardwareOperatingMode.Compatible
                : HardwareOperatingMode.Normal);

    private void HardwareProfile_Changed(object sender, RoutedEventArgs e)
    {
        if (!_hardwareProfileControlsReady)
            return;

        try
        {
            HardwareProfileService.SavePreferences(CurrentHardwarePreferences());
            ApplyCurrentHardwareProfile();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unable to persist hardware profile.", ex);
            HardwareProfileStatusText.Text =
                L("Unable to save hardware profile.", "Impossible d'enregistrer le profil matériel.");
        }
    }

    private async void RefreshHardwareProfile_Click(object sender, RoutedEventArgs e)
        => await RefreshHardwareProfileAsync();

    private async Task RefreshHardwareProfileAsync()
    {
        if (!_hardwareProfileControlsReady || _hardwareProbeBusy || !IsLoaded)
            return;

        _hardwareProbeBusy = true;
        HardwareProfileStatusText.Text = L(
            "Reading hardware and graphics APIs…",
            "Lecture du matériel et des API graphiques…");
        try
        {
            var snapshot = await Task.Run(_hardwareProfiles.Detect);
            if (!IsLoaded)
                return;

            var changed = _hardwareSnapshot?.Fingerprint != snapshot.Fingerprint;
            _hardwareSnapshot = snapshot;
            _lastHardwareProbe = DateTimeOffset.UtcNow;

            // Never rely on a manually selected preset to unlock RTX features.
            _gpu = snapshot.Gpu;
            _gpuCapabilities = GpuCapabilityService.Evaluate(_gpu);
            GpuText.Text = $"{_gpu.Name}  •  {_gpu.Generation}";
            GpuCompatibilityText.Text = _gpuCapabilities.Summary;
            ApplyCurrentHardwareProfile();

            if (changed)
                AppLogger.Info("GPU/driver/hardware fingerprint changed; profile recomputed.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Hardware profile probe failed.", ex);
            HardwareProfileStatusText.Text = L(
                "Unable to read hardware. No compatibility claims were made.",
                "Lecture du matériel impossible. Aucune compatibilité n'a été présumée.");
        }
        finally
        {
            _hardwareProbeBusy = false;
        }
    }

    private void ApplyCurrentHardwareProfile()
    {
        if (_hardwareSnapshot == null)
            return;

        var snapshot = _hardwareSnapshot;
        var decision = HardwareProfileService.Evaluate(
            snapshot, CurrentHardwarePreferences());

        PresetBox.IsEnabled = decision.AllowsNeuralRendering;
        AutoNvidiaRuntimeCheck.IsEnabled = decision.AllowsNeuralRendering;
        SelectRuntimeButton.IsEnabled = decision.AllowsNeuralRendering;
        GameDlssSrCheck.IsEnabled = decision.Capabilities.SuperResolution;
        GameDlssReflexCheck.IsEnabled = decision.Capabilities.IsSupportedRtx;
        GameDlssFgCheck.IsEnabled = decision.AllowsFrameGeneration;
        GameDlssNrCheck.IsEnabled = decision.AllowsNeuralRendering;

        if (!decision.AllowsFrameGeneration)
            GameDlssFgCheck.IsChecked = false;
        if (!decision.AllowsNeuralRendering)
        {
            GameDlssNrCheck.IsChecked = false;
            AutoNvidiaRuntimeCheck.IsChecked = false;
        }

        var mode = HardwareCompatibleCheck.IsChecked == true
            ? L("Compatible", "Compatible")
            : L("Normal", "Normal");
        HardwareProfileStatusText.Text = decision.UsesAutomaticFallback
            ? L(
                "Requested RTX 5060 Ti / Ryzen 9700X profile was not confirmed. Using actual detected hardware.",
                "Profil RTX 5060 Ti / Ryzen 9700X non confirmé. Utilisation du matériel réellement détecté.")
            : L(
                $"Hardware profile active • {mode}",
                $"Profil matériel actif • {mode}");

        var dx = snapshot.DirectX12RuntimePresent
            ? L("D3D12 runtime detected", "Runtime D3D12 détecté")
            : L("D3D12 runtime not detected", "Runtime D3D12 non détecté");
        var vk = snapshot.VulkanLoaderPresent
            ? L("Vulkan loader and ICD registry entry detected", "Chargeur Vulkan et entrée de pilote ICD détectés")
            : L("Vulkan loader/ICD not confirmed", "Chargeur Vulkan/ICD non confirmé");
        var ram = snapshot.InstalledRamBytes > 0
            ? $"{snapshot.InstalledRamBytes / 1024d / 1024d / 1024d:0.0} GiB"
            : L("Unknown", "Inconnue");
        var vram = snapshot.GpuVramMiB is long mib
            ? $"{mib / 1024d:0.0} GiB"
            : L("Unknown", "Inconnue");
        var driverNote = snapshot.NvidiaDriverVersion == "617.42"
            ? L(
                "NVIDIA 617.42 WHQL (6 Oct 2026) • NVIDIA reports a known issue: Prefer Maximum Performance mode may not apply correctly. Actual installed version detected through NVIDIA-SMI.",
                "NVIDIA 617.42 WHQL (6 oct. 2026) • Problème connu signalé par NVIDIA : le mode Performances maximales peut ne pas s'appliquer correctement. Version installée détectée par NVIDIA-SMI.")
            : L("Driver information is read from NVIDIA-SMI when available.",
                "La version du pilote provient de NVIDIA-SMI lorsqu'il est disponible.");

        HardwareDetailsText.Text = string.Join(Environment.NewLine, new[]
        {
            $"{L("GPU", "GPU")}: {snapshot.Gpu.Name} ({snapshot.Gpu.Generation}, {snapshot.GpuArchitecture})",
            $"{L("VRAM", "VRAM")}: {vram}",
            $"{L("CPU", "CPU")}: {snapshot.CpuName} ({snapshot.CpuArchitecture})",
            $"{L("RAM", "RAM")}: {ram}",
            $"{L("NVIDIA driver", "Pilote NVIDIA")}: {snapshot.NvidiaDriverVersion}",
            $"DirectX: {dx}",
            $"Vulkan: {vk}",
            $"{L("DLSS Super Resolution", "DLSS Super Resolution")}: {decision.Capabilities.SuperResolution}",
            $"Frame Generation: {decision.AllowsFrameGeneration}",
            $"Neural Rendering: {decision.AllowsNeuralRendering}",
            driverNote
        });
    }

    private string L(
        string english,
        string french)
        => UiLocalizationService.NormalizeLanguage(_uiLanguage) == "fr"
            ? french
            : english;

}
