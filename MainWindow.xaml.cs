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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _gpu = _gpus.Detect();
        GpuText.Text = $"{_gpu.Name}  •  {_gpu.Generation}";
        GamePathBox.Text = _games.DetectCyberpunk() ?? "";
        await RefreshReleaseAsync();
        RefreshState();
    }

    private async Task RefreshReleaseAsync()
    {
        try
        {
            AvailableVersionText.Text = "Checking GitHub…";
            _release = await _releases.GetLatestAsync(IsPrereleaseSelected());
            AvailableVersionText.Text = _release == null ? "No compatible ZIP release found" : $"Available: {_release.Tag}";
        }
        catch (Exception ex) { AvailableVersionText.Text = $"Release check failed: {ex.Message}"; }
    }

    private void RefreshState()
    {
        var game = GamePathBox.Text;
        if (string.IsNullOrWhiteSpace(game) || !Directory.Exists(game))
        {
            StatusText.Text = "Cyberpunk 2077 not detected";
            VersionText.Text = "";
            RuntimeText.Text = "";
            InstallButton.IsEnabled = false;
            UpdateButton.IsEnabled = false;
            return;
        }

        var state = _installer.Inspect(game, _gpu.Generation);
        StatusText.Text = state.Installed ? $"Installed • proxy {state.ProxyName}" : "Not installed";
        VersionText.Text = $"Installed version: {state.Version ?? "unknown"}";
        RuntimeText.Text = !state.RuntimePresent ? "DLSSNR runtime: missing" :
            $"DLSSNR runtime: {(state.RuntimeHashValid ? "valid" : "hash invalid")} • {state.RuntimeHash}";
        InstallButton.IsEnabled = !state.Installed;
        UpdateButton.IsEnabled = state.Installed;
        LogBox.Text = _installer.ReadLog(game);
    }

    private void SelectGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Cyberpunk 2077 root or bin\\x64 folder" };
        if (dialog.ShowDialog() != true) return;
        var normalized = GameDetectionService.Normalize(dialog.FolderName);
        if (normalized == null)
        {
            MessageBox.Show("Cyberpunk2077.exe was not found. Select the game root or bin\\x64 folder.", "DLSS NR Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        GamePathBox.Text = normalized;
        RefreshState();
    }

    private async void SelectRuntime_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select nvngx_dlssnr.dll", Filter = "DLSSNR runtime (nvngx_dlssnr.dll)|nvngx_dlssnr.dll|DLL files (*.dll)|*.dll" };
        if (dialog.ShowDialog() != true) return;
        _runtimePath = dialog.FileName;
        var hash = await HashService.Sha256Async(_runtimePath);
        var expected = _gpu.Generation == "RTX 50" ? InstallerService.Rtx50Hash :
            _gpu.Generation is "RTX 20" or "RTX 30" or "RTX 40" ? InstallerService.Rtx2040Hash : null;
        RuntimePathText.Text = $"{Path.GetFileName(_runtimePath)}\nSHA-256: {hash}\n{(expected != null && hash.Equals(expected, StringComparison.OrdinalIgnoreCase) ? "Valid runtime" : "Hash does not match detected GPU generation")}";
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_release == null) { MessageBox.Show("No release selected/available."); return; }
        if (string.IsNullOrWhiteSpace(_runtimePath) || !File.Exists(_runtimePath)) { MessageBox.Show("Select your nvngx_dlssnr.dll first."); return; }
        if (string.IsNullOrWhiteSpace(GamePathBox.Text)) return;
        var proxy = ((ComboBoxItem)ProxyBox.SelectedItem).Content?.ToString() ?? "dbghelp.dll";

        try
        {
            SetBusy(true);
            var backup = await _installer.InstallAsync(GamePathBox.Text, _runtimePath, _gpu, _release, proxy, _releases);
            MessageBox.Show($"Installation completed.\nBackup: {backup}", "DLSS NR Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshState();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Installation failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { SetBusy(false); }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        try { _installer.RestoreLatest(GamePathBox.Text); RefreshState(); MessageBox.Show("Latest backup restored."); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Remove files managed by DLSS NR Manager? Backups will be preserved.", "Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { _installer.Uninstall(GamePathBox.Text); RefreshState(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Uninstall failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) { await RefreshReleaseAsync(); RefreshState(); }
    private void ReloadLog_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(GamePathBox.Text)) LogBox.Text = _installer.ReadLog(GamePathBox.Text); }
    private async void ChannelBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await RefreshReleaseAsync(); }
    private bool IsPrereleaseSelected() => ChannelBox.SelectedIndex == 1;
    private void SetBusy(bool busy) { InstallButton.IsEnabled = !busy; UpdateButton.IsEnabled = !busy; Cursor = busy ? System.Windows.Input.Cursors.Wait : null; }
}